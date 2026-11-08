using Maran.Modules.Sites.Commands.ReplacePhpSettings;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Tests.Commands.ReplacePhpSettings;

/// <summary>Behavioral contract of <see cref="ReplacePhpSettingsCommandHandler"/>.</summary>
public sealed class ReplacePhpSettingsCommandHandlerTests
{
    /// <summary>Saving settings rewrites every pool of the account, then stores the rows, then journals.</summary>
    [Fact]
    public async Task Saving_settings_rewrites_every_pool_then_stores_the_rows_and_journals()
    {
        var world = World.Create(versions: ["8.3", "8.4"]);

        var result = await world.Handler().HandleAsync(Command(world, ("memory_limit", "128")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["8.3", "8.4"], world.Php.PoolCalls.Select(call => { return call.Version; }).ToList());
        Assert.All(world.Php.PoolCalls, call => { Assert.Equal("memory_limit", Assert.Single(call.Settings).Name); });
        Assert.Equal("128M", Assert.Single(world.Php.PoolCalls[0].Settings).Value);
        Assert.Equal("128M", (await world.Context.PhpSettings.SingleAsync()).Value);
        Assert.Equal(SiteAuditActions.PhpSettingsChanged, Assert.Single(world.Audit.Entries).Action);
    }

    /// <summary>A value above the plan is refused and nothing is written anywhere.</summary>
    [Fact]
    public async Task A_value_above_the_plan_is_refused_and_nothing_is_written()
    {
        var world = World.Create(versions: ["8.3"]);

        var result = await world.Handler().HandleAsync(Command(world, ("memory_limit", "257")), CancellationToken.None);

        Assert.Equal("PhpSettingAbovePlan", result.Error!.Code);
        Assert.Empty(world.Php.PoolCalls);
        Assert.Empty(await world.Context.PhpSettings.ToListAsync());
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A failure at the second version puts the first back and stores nothing.</summary>
    [Fact]
    public async Task A_failure_at_the_second_version_puts_the_first_back_and_stores_nothing()
    {
        var world = World.Create(versions: ["8.3", "8.4"]);
        world.Php.PoolFailures["8.4"] = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await world.Handler().HandleAsync(Command(world, ("memory_limit", "128")), CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(["8.3", "8.4", "8.3"], world.Php.PoolCalls.Select(call => { return call.Version; }).ToList());
        Assert.Empty(world.Php.PoolCalls[2].Settings);
        Assert.Empty(await world.Context.PhpSettings.ToListAsync());
    }

    /// <summary>A setting left out of the request is removed and goes back to the default.</summary>
    [Fact]
    public async Task A_setting_left_out_of_the_request_is_removed()
    {
        var world = World.Create(versions: ["8.3"]);
        world.Context.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), world.AccountId, "memory_limit", "128M"));
        await world.Context.SaveChangesAsync();

        var result = await world.Handler().HandleAsync(Command(world), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await world.Context.PhpSettings.ToListAsync());
        Assert.Empty(Assert.Single(world.Php.PoolCalls).Settings);
    }

    /// <summary>Repeating a request the pools already carry writes nothing to the host.</summary>
    [Fact]
    public async Task Repeating_a_request_writes_nothing_to_the_host()
    {
        var world = World.Create(versions: ["8.3"]);
        await world.Handler().HandleAsync(Command(world, ("memory_limit", "128")), CancellationToken.None);
        world.Php.PoolCalls.Clear();

        var again = await world.Handler().HandleAsync(Command(world, ("memory_limit", "128")), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Empty(world.Php.PoolCalls);
    }

    /// <summary>An account the caller does not own answers not found and touches nothing.</summary>
    [Fact]
    public async Task An_account_the_caller_does_not_own_answers_not_found()
    {
        var world = World.Create(versions: ["8.3"]);

        var result = await world.Handler().HandleAsync(
            new ReplacePhpSettingsCommand([new PhpSettingInputDto("memory_limit", "64")], Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(world.Php.PoolCalls);
        Assert.Empty(await world.Context.PhpSettings.ToListAsync());
    }

    /// <summary>Another customer's settings are invisible to the tenant-scoped context.</summary>
    [Fact]
    public async Task Another_customers_settings_are_invisible_to_the_tenant_scoped_context()
    {
        var world = World.Create(versions: ["8.3"]);
        world.Context.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), Guid.NewGuid(), "memory_limit", "64M"));
        await world.Context.SaveChangesAsync();

        Assert.Empty(await world.Context.PhpSettings.ToListAsync());
    }

    /// <summary>Builds a command for the world's account.</summary>
    /// <param name="world">The test world.</param>
    /// <param name="settings">The pairs to send.</param>
    private static ReplacePhpSettingsCommand Command(World world, params (string Name, string Value)[] settings)
    {
        return new ReplacePhpSettingsCommand(
            settings.Select(pair => { return new PhpSettingInputDto(pair.Name, pair.Value); }).ToList(),
            world.AccountId,
            "198.51.100.7",
            "tests");
    }

    /// <summary>The doubles one test runs against.</summary>
    private sealed class World
    {
        /// <summary>The account the caller owns.</summary>
        public Guid AccountId { get; } = Guid.NewGuid();

        /// <summary>The caller's tenant-scoped context.</summary>
        public SitesDbContext Context { get; private init; } = null!;

        /// <summary>The agent PHP double.</summary>
        public RecordingAgentPhpClient Php { get; } = new();

        /// <summary>The journal's writer.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The account directory double.</summary>
        private StubAccountDirectory Accounts { get; init; } = null!;

        /// <summary>Builds a world with a PHP site per version.</summary>
        /// <param name="versions">The versions the account has a PHP site on.</param>
        public static World Create(string[] versions)
        {
            var accountId = Guid.NewGuid();
            var database = Guid.NewGuid().ToString();
            using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
            {
                foreach (var version in versions)
                {
                    seed.Sites.Add(SitesTestContext.PhpSite(accountId, $"site-{version}.example", version));
                }

                seed.SaveChanges();
            }

            var snapshot = Snapshots.Account(accountId, "acme") with { };
            return new World(accountId)
            {
                Context = SitesTestContext.Create(FakeCurrentUser.Customer(accountId), database),
                Accounts = new StubAccountDirectory(snapshot),
            };
        }

        /// <summary>Builds the handler under test over this world's doubles.</summary>
        public ReplacePhpSettingsCommandHandler Handler()
        {
            var factory = new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance);
            var displayNames = new StringLocalizer<Maran.Modules.Sites.Resources.DisplayNames>(factory);

            return new ReplacePhpSettingsCommandHandler(
                Context,
                Accounts,
                new PhpAccountPoolSettings(Context),
                new PhpPoolApplier(Php),
                new PhpSettingsReader(Context, displayNames),
                new SiteAuditJournal(Audit, FakeCurrentUser.Customer(AccountId)));
        }

        /// <summary>Creates the world around an account id.</summary>
        /// <param name="accountId">The account the caller owns.</param>
        private World(Guid accountId)
        {
            AccountId = accountId;
        }
    }
}
