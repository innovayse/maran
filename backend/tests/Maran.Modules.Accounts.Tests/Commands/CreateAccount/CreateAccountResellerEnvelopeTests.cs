using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// What a reseller's envelope allows when an account is created: the account count and the disk pool,
/// both inclusive, the suspended reseller, the slot a concurrent creation took, and the names that
/// are one namespace across every tenant.
/// </summary>
public sealed class CreateAccountResellerEnvelopeTests : IDisposable
{
    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The world the current test built; disposed with the class.</summary>
    private ResellerAccountsWorld? _world;

    /// <inheritdoc />
    public void Dispose()
    {
        _world?.Dispose();
    }

    /// <summary>Builds the world with the envelope the test is about.</summary>
    /// <param name="maxAccounts">The account limit of the first reseller; it already owns one account.</param>
    /// <param name="diskPoolMb">The disk pool of the first reseller; its one account already takes 1,000 MB.</param>
    /// <returns>The world.</returns>
    private ResellerAccountsWorld Build(int maxAccounts = 10, long diskPoolMb = 100_000)
    {
        _world = new ResellerAccountsWorld(
            ResellerTestData.Envelope(maxAccounts: maxAccounts, diskPoolMb: diskPoolMb));
        return _world;
    }

    /// <summary>Creates account <paramref name="name"/> as the first reseller on its own plan.</summary>
    /// <param name="world">The world.</param>
    /// <param name="name">The account's name.</param>
    /// <param name="interceptor">An interceptor to arm, or null.</param>
    /// <returns>The handler's result.</returns>
    private async Task<Result<AccountDto>> CreateAsync(
        ResellerAccountsWorld world,
        string name,
        ArmedSaveFailureInterceptor? interceptor = null)
    {
        var dbContext = interceptor is null
            ? world.ContextFor(world.UserA)
            : world.ContextFor(world.UserA, interceptor);
        var handler = AccountHandlerFactory.Create(dbContext, world.UserA, _agent, _audit);

        return await handler.HandleAsync(
            new CreateAccountCommand(
                name,
                $"{name}.example.com",
                world.PlanA.Id,
                "owner@example.com",
                IpAddress: "203.0.113.7",
                UserAgent: "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>The account that takes the last permitted slot is accepted, and the next one is refused.</summary>
    [Fact]
    public async Task The_account_that_takes_the_last_permitted_slot_is_accepted_and_the_next_one_is_refused()
    {
        var world = Build(maxAccounts: 2);

        var last = await CreateAsync(world, "second");
        var over = await CreateAsync(world, "third");

        Assert.True(last.IsSuccess);
        Assert.Equal("ResellerAccountLimitReached", over.Error!.Code);
        Assert.Equal(ErrorType.Conflict, over.Error.Type);
    }

    /// <summary>An account refused for the account limit provisions nothing and is journalled as a failure.</summary>
    [Fact]
    public async Task An_account_refused_for_the_account_limit_provisions_nothing_and_is_journalled_as_a_failure()
    {
        var world = Build(maxAccounts: 1);

        var result = await CreateAsync(world, "second");

        Assert.Equal("ResellerAccountLimitReached", result.Error!.Code);
        Assert.Empty(_agent.Calls);
        var owned = await world.ContextFor(FakeCurrentUser.Admin())
            .Accounts.CountAsync(a => a.ResellerId == world.ResellerA.Id);
        Assert.Equal(1, owned);
        var entry = Assert.Single(_audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("alpha-login", entry.ActorUsername);
    }

    /// <summary>A suspended account still holds its slot against the limit.</summary>
    [Fact]
    public async Task A_suspended_account_still_holds_its_slot_against_the_limit()
    {
        var world = Build(maxAccounts: 1);
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        (await admin.Accounts.SingleAsync(a => a.Id == world.AccountA.Id)).Suspend(SuspensionActor.Administrator);
        await admin.SaveChangesAsync();

        var result = await CreateAsync(world, "second");

        Assert.Equal("ResellerAccountLimitReached", result.Error!.Code);
    }

    /// <summary>An account whose plan brings the pool exactly to its size is accepted, and one more megabyte is refused.</summary>
    [Fact]
    public async Task An_account_that_brings_the_pool_exactly_to_its_size_is_accepted_and_the_next_is_refused()
    {
        var world = Build(diskPoolMb: 2_000);

        var exact = await CreateAsync(world, "second");
        var over = await CreateAsync(world, "third");

        Assert.True(exact.IsSuccess);
        Assert.Equal("ResellerDiskPoolExceeded", over.Error!.Code);
        Assert.Equal(ErrorType.Conflict, over.Error.Type);
        Assert.DoesNotContain(_agent.Calls, call =>
        {
            return call.StartsWith("create:third", StringComparison.Ordinal);
        });
    }

    /// <summary>A pool one megabyte short of the new account's disk refuses it.</summary>
    [Fact]
    public async Task A_pool_one_megabyte_short_of_the_new_accounts_disk_refuses_it()
    {
        var world = Build(diskPoolMb: 1_999);

        var result = await CreateAsync(world, "second");

        Assert.Equal("ResellerDiskPoolExceeded", result.Error!.Code);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A suspended reseller's live token cannot create an account.</summary>
    [Fact]
    public async Task A_suspended_resellers_live_token_cannot_create_an_account()
    {
        var world = Build();
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        (await admin.Resellers.SingleAsync(r => r.Id == world.ResellerA.Id)).Suspend();
        await admin.SaveChangesAsync();

        var result = await CreateAsync(world, "second");

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_agent.Calls);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A creation that loses the race for the last slot is refused as the limit, and its system user is removed again.</summary>
    [Fact]
    public async Task A_creation_that_loses_the_race_for_the_last_slot_is_refused_and_its_system_user_removed()
    {
        var world = Build(maxAccounts: 2);
        var interceptor = new ArmedSaveFailureInterceptor { Next = new DbUpdateConcurrencyException("race") };

        var result = await CreateAsync(world, "second", interceptor);

        Assert.Equal("ResellerAccountLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("delete:second", _agent.Calls[^1]);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
        Assert.Equal(0, await world.ContextFor(FakeCurrentUser.Admin()).Accounts.CountAsync(a => a.Name == "second"));
    }

    /// <summary>A name that another reseller's account holds is taken, though the creating reseller cannot see that account.</summary>
    [Fact]
    public async Task A_name_another_resellers_account_holds_is_taken_though_the_creating_reseller_cannot_see_it()
    {
        var world = Build();

        var result = await CreateAsync(world, world.AccountB.Name);

        Assert.Equal("AccountNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A name that a reseller holds is taken, because a reseller's name is also its login name.</summary>
    [Fact]
    public async Task A_name_a_reseller_holds_is_taken_because_it_is_also_a_login_name()
    {
        var world = Build();

        var result = await CreateAsync(world, world.ResellerB.Name);

        Assert.Equal("AccountNameTaken", result.Error!.Code);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A domain another reseller's account holds is taken, though the creating reseller cannot see that account.</summary>
    [Fact]
    public async Task A_domain_another_resellers_account_holds_is_taken_though_the_creating_reseller_cannot_see_it()
    {
        var world = Build();
        var dbContext = world.ContextFor(world.UserA);
        var handler = AccountHandlerFactory.Create(dbContext, world.UserA, _agent, _audit);

        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                "fresh",
                world.AccountB.PrimaryDomain,
                world.PlanA.Id,
                "owner@example.com",
                IpAddress: "203.0.113.7",
                UserAgent: "unit-tests"),
            CancellationToken.None);

        Assert.Equal("AccountDomainTaken", result.Error!.Code);
    }

    /// <summary>The refusal for a taken name is journalled with the reseller as actor and the audit action of a creation.</summary>
    [Fact]
    public async Task The_refusal_for_a_taken_name_is_journalled_with_the_reseller_as_actor()
    {
        var world = Build();

        await CreateAsync(world, world.AccountB.Name);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.AccountCreated, entry.Action);
        Assert.Equal("alpha-login", entry.ActorUsername);
        Assert.False(entry.Succeeded);
    }
}
