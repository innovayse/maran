using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Sites.Tests.IntegrationEvents.Handlers;

/// <summary>Behavioral contract of <see cref="AccountPlanChangedHandler"/>.</summary>
public sealed class AccountPlanChangedHandlerTests
{
    /// <summary>A lowered bound reaches every pool, cutting the stored value without touching the row.</summary>
    [Fact]
    public async Task A_lowered_bound_reaches_every_pool_and_keeps_the_stored_row()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example", "8.3"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "b.example", "8.4"));
        context.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), accountId, "memory_limit", "512M"));
        await context.SaveChangesAsync();
        var php = new RecordingAgentPhpClient();
        var account = Snapshots.Account(accountId, "acme") with { PhpLimits = new PhpLimitsSnapshot(128, 64, 120, 3000) };

        await Handler(context, php, account).HandleAsync(new AccountPlanChanged(accountId), CancellationToken.None);

        Assert.Equal(["8.3", "8.4"], php.PoolCalls.Select(call => { return call.Version; }).ToList());
        Assert.All(php.PoolCalls, call => { Assert.Equal("128M", Assert.Single(call.Settings).Value); });
        Assert.Equal("512M", context.PhpSettings.Single().Value);
    }

    /// <summary>An account with no PHP site has no pool and nothing is written.</summary>
    [Fact]
    public async Task An_account_with_no_php_site_has_nothing_written()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var php = new RecordingAgentPhpClient();

        await Handler(context, php, Snapshots.Account(accountId, "acme"))
            .HandleAsync(new AccountPlanChanged(accountId), CancellationToken.None);

        Assert.Empty(php.PoolCalls);
    }

    /// <summary>An agent refusal is logged away and never thrown, because the plan change is committed.</summary>
    [Fact]
    public async Task An_agent_refusal_never_throws()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example", "8.3"));
        await context.SaveChangesAsync();
        var php = new RecordingAgentPhpClient();
        php.PoolFailures["8.3"] = Maran.SharedKernel.Results.Error.Of("AgentSystemFailure", Maran.SharedKernel.Results.ErrorType.Failure);

        await Handler(context, php, Snapshots.Account(accountId, "acme"))
            .HandleAsync(new AccountPlanChanged(accountId), CancellationToken.None);

        Assert.Single(php.PoolCalls);
    }

    /// <summary>An account the directory cannot read is skipped.</summary>
    [Fact]
    public async Task An_account_the_directory_cannot_read_is_skipped()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var php = new RecordingAgentPhpClient();

        await Handler(context, php).HandleAsync(new AccountPlanChanged(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(php.PoolCalls);
    }

    /// <summary>Builds the handler over the doubles.</summary>
    /// <param name="context">The context the settings and sites are read from.</param>
    /// <param name="php">The agent PHP double.</param>
    /// <param name="accounts">The accounts the directory knows.</param>
    private static AccountPlanChangedHandler Handler(
        Maran.Modules.Sites.Persistence.SitesDbContext context,
        RecordingAgentPhpClient php,
        params AccountSnapshot[] accounts)
    {
        return new AccountPlanChangedHandler(
            new StubAccountDirectory(accounts),
            new PhpAccountPoolSettings(context),
            new PhpPoolApplier(php),
            NullLogger<AccountPlanChangedHandler>.Instance);
    }
}
