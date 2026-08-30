using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// Behavioral contract of <see cref="CreateAccountCommandHandler"/>. Runs against a real
/// <see cref="AccountsDbContext"/> backed by the EF Core InMemory provider — the handler's own
/// dependency, not a hand-rolled repository double — so the query logic (name/domain uniqueness
/// checks) is exercised as written; each test gets its own uniquely-named database so tests never
/// share state (rules/testing.md "Determinism").
/// </summary>
public sealed class CreateAccountCommandHandlerTests
{
    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Creating account with an unused name and domain inserts the row.</summary>
    [Fact]
    public async Task Creating_account_with_an_unused_name_and_domain_inserts_the_row()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(new CreateAccountCommand("acme", "acme.example.com", planId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Name);
        Assert.Equal("acme.example.com", result.Value.PrimaryDomain);
        Assert.Equal(planId, result.Value.PlanId);
        Assert.Equal(AccountStatus.Active, result.Value.Status);
        Assert.Equal(clock.UtcNow, result.Value.CreatedAt);

        var stored = await dbContext.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
    }

    /// <summary>Creating account with a taken name returns the typed name taken error without throwing.</summary>
    [Fact]
    public async Task Creating_account_with_a_taken_name_returns_the_typed_name_taken_error_without_throwing()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        dbContext.Accounts.Add(new Account(Guid.NewGuid(), "acme", "existing.example.com", Guid.NewGuid(), clock.UtcNow));
        await dbContext.SaveChangesAsync();
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "different.example.com", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNameTaken", result.Error!.Code);
    }

    /// <summary>Creating account with a taken domain returns the typed domain taken error without throwing.</summary>
    [Fact]
    public async Task Creating_account_with_a_taken_domain_returns_the_typed_domain_taken_error_without_throwing()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        dbContext.Accounts.Add(new Account(Guid.NewGuid(), "existing", "acme.example.com", Guid.NewGuid(), clock.UtcNow));
        await dbContext.SaveChangesAsync();
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("newname", "acme.example.com", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountDomainTaken", result.Error!.Code);
    }

    /// <summary>Creating an account provisions the system user with the plan's quota.</summary>
    /// <remarks>
    /// This test used to assert the opposite — that the handler's constructor took the context and
    /// the clock and nothing else — because when it was written the agent had no Accounts
    /// operations. It froze a temporary absence into a rule, and it would have failed the change
    /// that gave the account a system user to exist as. An Account IS a Linux user (spec §8); a
    /// row without one is a customer sold something that is not there.
    /// </remarks>
    [Fact]
    public async Task Creating_an_account_provisions_the_system_user_with_the_plans_quota()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);
        var planId = await SeedPlanAsync(dbContext, diskQuotaMb: 5_120);

        await handler.HandleAsync(new CreateAccountCommand("acme", "acme.example.com", planId), CancellationToken.None);

        Assert.Equal($"create:acme:{5_120UL * 1024 * 1024}", Assert.Single(agent.Calls));
    }

    /// <summary>A refused provisioning leaves no account row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_account_row_behind()
    {
        // The agent runs first precisely so this is the failure mode: nothing was written, and the
        // caller sees the agent's own typed error rather than an account that does not exist.
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient(Error.Of("AgentUnavailable"));
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(await dbContext.Accounts.ToListAsync());
    }

    /// <summary>An unknown plan is refused before the agent is asked to do anything.</summary>
    [Fact]
    public async Task An_unknown_plan_is_refused_before_the_agent_is_asked_to_do_anything()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        var handler = new CreateAccountCommandHandler(dbContext, agent, clock);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>Seeds one plan and returns its id.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="diskQuotaMb">The plan's disk allowance.</param>
    /// <returns>The seeded plan's id.</returns>
    private static async Task<Guid> SeedPlanAsync(AccountsDbContext dbContext, int diskQuotaMb = 1_024)
    {
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", diskQuotaMb, 5, 2, 3);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan.Id;
    }
}
