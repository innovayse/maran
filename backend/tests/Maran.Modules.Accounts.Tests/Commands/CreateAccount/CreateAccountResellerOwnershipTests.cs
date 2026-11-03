using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// Who owns an account when it is created: a reseller always creates for itself, an administrator
/// names the owner or none, and an account always sits on a plan of the same owner.
/// </summary>
public sealed class CreateAccountResellerOwnershipTests : IDisposable
{
    /// <summary>The world of two resellers and the administrator's plan.</summary>
    private readonly ResellerAccountsWorld _world = new();

    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>Runs a creation as <paramref name="caller"/>.</summary>
    /// <param name="caller">The caller the creation runs as.</param>
    /// <param name="planId">The plan to create on.</param>
    /// <param name="resellerId">The owner the request names, or null.</param>
    /// <param name="name">The account's name.</param>
    /// <returns>The handler's result and an administrator's context to read the stored rows back.</returns>
    private async Task<(Result<AccountDto> Result, AccountsDbContext Admin)> RunAsync(
        ICurrentUser caller,
        Guid planId,
        Guid? resellerId,
        string name = "newcomer")
    {
        var dbContext = _world.ContextFor(caller);
        var handler = AccountHandlerFactory.Create(dbContext, caller, _agent, _audit);
        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                name,
                $"{name}.example.com",
                planId,
                "owner@example.com",
                ResellerId: resellerId,
                IpAddress: "203.0.113.7",
                UserAgent: "unit-tests"),
            CancellationToken.None);

        return (result, _world.ContextFor(FakeCurrentUser.Admin()));
    }

    /// <summary>A reseller that names no owner creates an account it owns, on its own plan, and the answer names it.</summary>
    [Fact]
    public async Task A_reseller_that_names_no_owner_creates_an_account_it_owns_on_its_own_plan()
    {
        var (result, db) = await RunAsync(_world.UserA, _world.PlanA.Id, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(_world.ResellerA.Id, result.Value.ResellerId);
        Assert.Equal("alpha", result.Value.ResellerName);
        var stored = await db.Accounts.SingleAsync(account => account.Name == "newcomer");
        Assert.Equal(_world.ResellerA.Id, stored.ResellerId);
        Assert.Equal(_world.PlanA.ResellerId, stored.ResellerId);
        Assert.Contains($"create:newcomer:{1_000UL * 1024 * 1024}", _agent.Calls);
    }

    /// <summary>A reseller that names itself as the owner is accepted, the same as naming none.</summary>
    [Fact]
    public async Task A_reseller_that_names_itself_as_the_owner_is_accepted()
    {
        var (result, _) = await RunAsync(_world.UserA, _world.PlanA.Id, _world.ResellerA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(_world.ResellerA.Id, result.Value.ResellerId);
    }

    /// <summary>A reseller naming another reseller as owner is answered not found, creates nothing and is journalled.</summary>
    [Fact]
    public async Task A_reseller_naming_another_reseller_as_owner_is_answered_not_found_and_creates_nothing()
    {
        var (result, db) = await RunAsync(_world.UserA, _world.PlanA.Id, _world.ResellerB.Id);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_agent.Calls);
        Assert.Equal(0, await db.Accounts.CountAsync(account => account.Name == "newcomer"));
        var entry = Assert.Single(_audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("alpha-login", entry.ActorUsername);
        Assert.Equal(AccountAuditActions.AccountCreated, entry.Action);
    }

    /// <summary>A reseller naming a reseller that does not exist gets the very same answer as naming another's.</summary>
    [Fact]
    public async Task A_reseller_naming_a_reseller_that_does_not_exist_gets_the_same_answer_as_naming_another()
    {
        var (other, _) = await RunAsync(_world.UserA, _world.PlanA.Id, _world.ResellerB.Id);
        var (missing, _) = await RunAsync(_world.UserA, _world.PlanA.Id, Guid.NewGuid());

        Assert.Equal(other.Error!.Code, missing.Error!.Code);
        Assert.Equal(other.Error.Type, missing.Error.Type);
    }

    /// <summary>A reseller creating on another reseller's plan is answered plan not found, like a plan that does not exist.</summary>
    [Fact]
    public async Task A_reseller_creating_on_another_resellers_plan_is_answered_plan_not_found()
    {
        var (foreign, _) = await RunAsync(_world.UserA, _world.PlanB.Id, null);
        var (missing, _) = await RunAsync(_world.UserA, Guid.NewGuid(), null);

        Assert.Equal("PlanNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(missing.Error!.Code, foreign.Error.Code);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A reseller creating on an administrator's plan is answered plan not found, because it cannot see that plan.</summary>
    [Fact]
    public async Task A_reseller_creating_on_an_administrators_plan_is_answered_plan_not_found()
    {
        var (result, _) = await RunAsync(_world.UserA, _world.AdminPlan.Id, null);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>An administrator naming a reseller and that reseller's plan creates the reseller's account.</summary>
    [Fact]
    public async Task An_administrator_naming_a_reseller_and_its_plan_creates_that_resellers_account()
    {
        var (result, db) = await RunAsync(FakeCurrentUser.Admin(), _world.PlanA.Id, _world.ResellerA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(_world.ResellerA.Id, result.Value.ResellerId);
        Assert.Equal("alpha", result.Value.ResellerName);
        Assert.Equal(_world.ResellerA.Id, (await db.Accounts.SingleAsync(a => a.Name == "newcomer")).ResellerId);
    }

    /// <summary>An administrator naming no owner on its own plan creates an account no reseller owns.</summary>
    [Fact]
    public async Task An_administrator_naming_no_owner_on_its_own_plan_creates_an_account_no_reseller_owns()
    {
        var (result, _) = await RunAsync(FakeCurrentUser.Admin(), _world.AdminPlan.Id, null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ResellerId);
        Assert.Null(result.Value.ResellerName);
    }

    /// <summary>An administrator naming a reseller but an administrator's plan is refused as a plan owner mismatch.</summary>
    [Fact]
    public async Task An_administrator_naming_a_reseller_but_an_administrators_plan_is_refused_as_a_plan_owner_mismatch()
    {
        var (result, _) = await RunAsync(FakeCurrentUser.Admin(), _world.AdminPlan.Id, _world.ResellerA.Id);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>An administrator naming no owner but a reseller's plan is refused as a plan owner mismatch.</summary>
    [Fact]
    public async Task An_administrator_naming_no_owner_but_a_resellers_plan_is_refused_as_a_plan_owner_mismatch()
    {
        var (result, _) = await RunAsync(FakeCurrentUser.Admin(), _world.PlanA.Id, null);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
    }

    /// <summary>An administrator naming a reseller that does not exist is answered reseller not found.</summary>
    [Fact]
    public async Task An_administrator_naming_a_reseller_that_does_not_exist_is_answered_reseller_not_found()
    {
        var (result, _) = await RunAsync(FakeCurrentUser.Admin(), _world.AdminPlan.Id, Guid.NewGuid());

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A success is journalled with the reseller's login as the actor.</summary>
    [Fact]
    public async Task A_resellers_creation_is_journalled_with_the_resellers_login_as_the_actor()
    {
        await RunAsync(_world.UserA, _world.PlanA.Id, null);

        var entry = Assert.Single(_audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("alpha-login", entry.ActorUsername);
        Assert.Equal("newcomer", entry.Subject);
    }
}
