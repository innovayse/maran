using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// What creating an account leaves in the audit journal. Creation is the one Accounts operation
/// whose refusals tell the caller something about other customers — "that name is taken", "that
/// domain is taken" — so the failures are journalled as carefully as the successes.
/// </summary>
public sealed class CreateAccountAuditTests
{
    private const string Ip = "203.0.113.7";
    private const string Client = "unit-tests";

    /// <summary>A created account is journalled as a success naming the account.</summary>
    [Fact]
    public async Task A_created_account_is_journalled_as_a_success_naming_the_account()
    {
        var world = await WorldAsync();

        var result = await world.CreateAsync("acme", "acme.example.com", world.PlanId);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AccountCreated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(Ip, entry.IpAddress);
        Assert.Equal(Client, entry.UserAgent);
    }

    /// <summary>A creation refused for a taken name is journalled as a failure.</summary>
    [Fact]
    public async Task A_creation_refused_for_a_taken_name_is_journalled_as_a_failure()
    {
        var world = await WorldAsync();
        await world.CreateAsync("acme", "acme.example.com", world.PlanId);
        world.Audit.Entries.Clear();

        var result = await world.CreateAsync("acme", "other.example.com", world.PlanId);

        Assert.Equal("AccountNameTaken", result.Error!.Code);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AccountCreated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A creation refused for an unknown plan is journalled as a failure.</summary>
    [Fact]
    public async Task A_creation_refused_for_an_unknown_plan_is_journalled_as_a_failure()
    {
        var world = await WorldAsync();

        var result = await world.CreateAsync("acme", "acme.example.com", Guid.NewGuid());

        Assert.Equal("PlanNotFound", result.Error!.Code);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("acme", entry.Subject);
    }

    /// <summary>A creation the agent refuses is journalled as a failure and never as a success.</summary>
    [Fact]
    public async Task A_creation_the_agent_refuses_is_journalled_as_a_failure_and_never_as_a_success()
    {
        var world = await WorldAsync(new RecordingAgentAccountsClient(Error.Of("AgentUnavailable", ErrorType.Unavailable)));

        var result = await world.CreateAsync("acme", "acme.example.com", world.PlanId);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AccountCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The journalled subject is the account name and never a home directory path.</summary>
    [Fact]
    public async Task The_journalled_subject_is_the_account_name_and_never_a_home_directory_path()
    {
        // The agent answers with "/home/acme": a path names the host's layout and belongs nowhere
        // in a journal that is never deleted (rules/security.md item 8).
        var world = await WorldAsync();

        await world.CreateAsync("acme", "acme.example.com", world.PlanId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("acme", entry.Subject);
        Assert.DoesNotContain("/", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Builds a world with one seeded plan and an empty journal.</summary>
    /// <param name="agent">The agent double to use; a succeeding one by default.</param>
    /// <returns>The assembled world.</returns>
    private static async Task<World> WorldAsync(RecordingAgentAccountsClient? agent = null)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new AccountsDbContext(options);

        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 1_024, 5, 2, 3, 5, 5);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();

        return new World(dbContext, agent ?? new RecordingAgentAccountsClient(), plan.Id);
    }

    /// <summary>One test's handler, its journal and the plan accounts are created against.</summary>
    private sealed class World
    {
        /// <summary>The handler under test.</summary>
        private readonly CreateAccountCommandHandler _handler;

        /// <summary>Assembles the world.</summary>
        /// <param name="dbContext">The module's context, in memory.</param>
        /// <param name="agent">The agent double.</param>
        /// <param name="planId">The seeded plan.</param>
        public World(AccountsDbContext dbContext, RecordingAgentAccountsClient agent, Guid planId)
        {
            Audit = new RecordingAuditWriter();
            PlanId = planId;
            _handler = new CreateAccountCommandHandler(
                dbContext,
                agent,
                new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                new AccountAuditJournal(Audit, FakeCurrentUser.Admin()));
        }

        /// <summary>Everything the handler journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The plan accounts are created against.</summary>
        public Guid PlanId { get; }

        /// <summary>Runs one creation.</summary>
        /// <param name="name">The account name.</param>
        /// <param name="domain">The primary domain.</param>
        /// <param name="planId">The plan to create against.</param>
        /// <returns>The handler's result.</returns>
        public Task<Result<AccountDto>> CreateAsync(string name, string domain, Guid planId)
        {
            return _handler.HandleAsync(
                new CreateAccountCommand(name, domain, planId, Ip, Client), CancellationToken.None);
        }
    }
}
