using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// How a new account is put under its plan's runtime ceilings (issue #133): right after the system user
/// exists, before the row, and undone with the user when the agent refuses them.
/// </summary>
public sealed class CreateAccountRuntimeLimitsTests
{
    /// <summary>A new account's slice is held to its plan's ceilings, after the system user exists and before the row.</summary>
    [Fact]
    public async Task A_new_accounts_slice_is_held_to_its_plans_ceilings()
    {
        await using var dbContext = AccountsTestContext.Create();
        var agent = new RecordingAgentAccountsClient();
        var planId = await SeedPlanAsync(dbContext);

        var result = await NewHandler(dbContext, agent).HandleAsync(Command(planId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [$"create:acme:{1_024UL * 1024 * 1024}", "limits:acme:Limited150/Unlimited0/Limited64"],
            agent.Calls);
    }

    /// <summary>A refused ceiling undoes the system user and stores no row, so the name stays creatable.</summary>
    [Fact]
    public async Task A_refused_ceiling_undoes_the_system_user_and_stores_no_row()
    {
        await using var dbContext = AccountsTestContext.Create();
        var agent = new RecordingAgentAccountsClient();
        agent.RuntimeLimitRefusals["acme"] = Error.Of("AgentSystemFailure", ErrorType.Failure);
        var planId = await SeedPlanAsync(dbContext);

        var result = await NewHandler(dbContext, agent).HandleAsync(Command(planId), CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal("delete:acme", agent.Calls[^1]);
        Assert.Empty(await dbContext.Accounts.ToListAsync());
    }

    /// <summary>Builds the handler over the given context and agent.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="agent">The agent double.</param>
    /// <returns>The handler.</returns>
    private static CreateAccountCommandHandler NewHandler(
        AccountsDbContext dbContext,
        RecordingAgentAccountsClient agent)
    {
        return new CreateAccountCommandHandler(
            dbContext,
            agent,
            new AccountRuntimeLimitsEnforcer(agent),
            new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new AccountAuditJournal(new RecordingAuditWriter(), FakeCurrentUser.Admin()),
            new StubMessageBus(),
            NullLogger<CreateAccountCommandHandler>.Instance);
    }

    /// <summary>Builds the creation of account <c>acme</c> on <paramref name="planId"/>.</summary>
    /// <param name="planId">The plan to create it on.</param>
    /// <returns>The command.</returns>
    private static CreateAccountCommand Command(Guid planId)
    {
        return new CreateAccountCommand(
            "acme", "acme.example.com", planId, "owner@example.com", IpAddress: "203.0.113.7", UserAgent: "unit-tests");
    }

    /// <summary>Seeds a plan with a CPU and a process ceiling and no memory ceiling.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <returns>The plan's id.</returns>
    private static async Task<Guid> SeedPlanAsync(AccountsDbContext dbContext)
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 1_024, 5, 2, 3, 0, 5, 5);
        plan.ChangeRuntimeLimits(RuntimeLimits.Of(
            ResourceLimit.Limited(150), ResourceLimit.Unlimited(), ResourceLimit.Limited(64)));
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan.Id;
    }
}
