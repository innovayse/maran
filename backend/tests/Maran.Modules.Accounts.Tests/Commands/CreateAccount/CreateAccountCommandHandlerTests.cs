using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
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
    /// <summary>The caller address every command in this file carries.</summary>
    private const string Ip = "203.0.113.7";

    /// <summary>The user agent every command in this file carries.</summary>
    private const string Client = "unit-tests";

    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options, FakeCurrentUser.Admin());
    }

    /// <summary>Creating account with an unused name and domain inserts the row.</summary>
    [Fact]
    public async Task Creating_account_with_an_unused_name_and_domain_inserts_the_row()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var agent = new RecordingAgentAccountsClient();
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client), CancellationToken.None);

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
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "different.example.com", Guid.NewGuid(), "owner@example.com", IpAddress: Ip, UserAgent: Client),
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
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("newname", "acme.example.com", Guid.NewGuid(), "owner@example.com", IpAddress: Ip, UserAgent: Client),
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
        var agent = new RecordingAgentAccountsClient();
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);
        var planId = await SeedPlanAsync(dbContext, diskQuotaMb: 5_120);

        await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client), CancellationToken.None);

        Assert.Equal(
            [$"create:acme:{5_120UL * 1024 * 1024}", "limits:acme:Unlimited0/Unlimited0/Unlimited0"],
            agent.Calls);
    }

    /// <summary>A refused provisioning leaves no account row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_account_row_behind()
    {
        // The agent runs first precisely so this is the failure mode: nothing was written, and the
        // caller sees the agent's own typed error rather than an account that does not exist.
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient(Error.Of("AgentUnavailable", ErrorType.Unavailable));
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(await dbContext.Accounts.ToListAsync());
    }

    /// <summary>
    /// An Identity subscriber that fails to issue the owner's login answers with the typed
    /// <c>AccountLoginIssuanceFailed</c> code rather than letting the exception propagate — and the
    /// account row it already wrote stays, because the failure is the login, not the creation.
    /// </summary>
    [Fact]
    public async Task A_subscriber_that_fails_to_issue_the_login_returns_the_typed_error_and_keeps_the_account_row()
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var bus = new StubMessageBus(new InvalidOperationException("SMTP relay unreachable"));
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent, bus: bus);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountLoginIssuanceFailed", result.Error!.Code);
        Assert.DoesNotContain("SMTP relay unreachable", result.Error!.Code, StringComparison.Ordinal);

        var stored = await dbContext.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
    }

    /// <summary>An unknown plan is refused before the agent is asked to do anything.</summary>
    [Fact]
    public async Task An_unknown_plan_is_refused_before_the_agent_is_asked_to_do_anything()
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", Guid.NewGuid(), "owner@example.com", IpAddress: Ip, UserAgent: Client), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>
    /// A weak initial password is refused before the agent is asked anything and before any row is
    /// written: no account is left behind with a login that was never issued (issue #194).
    /// </summary>
    [Fact]
    public async Task A_weak_initial_password_is_refused_before_the_agent_or_the_database_is_touched()
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var bus = new StubMessageBus();
        var policy = new StubInitialPasswordPolicy("weak");
        var handler = AccountHandlerFactory.Create(
            dbContext, FakeCurrentUser.Admin(), agent, bus: bus, passwordPolicy: policy);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                "acme",
                "acme.example.com",
                planId,
                "owner@example.com",
                LoginMode: OwnerLoginMode.InitialPassword,
                InitialPassword: new SensitiveString("weak"),
                IpAddress: Ip,
                UserAgent: Client),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountInitialPasswordTooShort", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(["acme"], policy.AskedFor);
        Assert.Empty(agent.Calls);
        Assert.Empty(await dbContext.Accounts.ToListAsync());
        Assert.Empty(bus.Invoked);
    }

    /// <summary>
    /// Each rule the policy can name is answered with its own code, never with the sentence about
    /// choosing a sign-in mode (issue #512): a too-short password must not read as "no method chosen".
    /// </summary>
    /// <param name="verdict">The rule the password breaks.</param>
    /// <param name="code">The code the refusal must carry.</param>
    [Theory]
    [InlineData(InitialPasswordVerdict.TooShort, "AccountInitialPasswordTooShort")]
    [InlineData(InitialPasswordVerdict.TooLong, "AccountInitialPasswordTooLong")]
    [InlineData(InitialPasswordVerdict.SameAsUsername, "AccountInitialPasswordSameAsName")]
    public async Task A_refused_initial_password_is_answered_with_the_code_of_the_rule_it_broke(
        InitialPasswordVerdict verdict,
        string code)
    {
        await using var dbContext = CreateDbContext();
        var policy = new StubInitialPasswordPolicy("weak", verdict);
        var handler = AccountHandlerFactory.Create(
            dbContext, FakeCurrentUser.Admin(), new RecordingAgentAccountsClient(), passwordPolicy: policy);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                "acme",
                "acme.example.com",
                planId,
                "owner@example.com",
                LoginMode: OwnerLoginMode.InitialPassword,
                InitialPassword: new SensitiveString("weak"),
                IpAddress: Ip,
                UserAgent: Client),
            CancellationToken.None);

        Assert.Equal(code, result.Error!.Code);
        Assert.NotEqual("AccountInitialPasswordInvalid", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>
    /// The inverse control: an initial password the policy accepts creates the account, so the refusal
    /// above is the policy's verdict and not a handler that refuses every initial password.
    /// </summary>
    [Fact]
    public async Task An_initial_password_the_policy_accepts_creates_the_account()
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var policy = new StubInitialPasswordPolicy("weak");
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent, passwordPolicy: policy);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                "acme",
                "acme.example.com",
                planId,
                "owner@example.com",
                LoginMode: OwnerLoginMode.InitialPassword,
                InitialPassword: new SensitiveString("Correct-Horse-9"),
                IpAddress: Ip,
                UserAgent: Client),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["acme"], policy.AskedFor);
        Assert.Single(await dbContext.Accounts.ToListAsync());
    }

    /// <summary>An invitation carries no password, so the password policy is never asked.</summary>
    [Fact]
    public async Task An_invitation_never_asks_the_password_policy()
    {
        await using var dbContext = CreateDbContext();
        var policy = new StubInitialPasswordPolicy();
        var handler = AccountHandlerFactory.Create(
            dbContext, FakeCurrentUser.Admin(), new RecordingAgentAccountsClient(), passwordPolicy: policy);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(policy.AskedFor);
    }

    /// <summary>
    /// An owner e-mail address another login already holds is refused with a typed conflict before the
    /// agent is asked anything and before any row is written (issue #485): no account is left behind
    /// without a login.
    /// </summary>
    /// <param name="conflict">The clash the directory reports.</param>
    /// <param name="code">The code the creation must answer with.</param>
    [Theory]
    [InlineData(OwnerLoginConflict.EmailTaken, "AccountOwnerEmailTaken")]
    [InlineData(OwnerLoginConflict.UsernameTaken, "AccountLoginNameTaken")]
    public async Task An_owner_login_another_login_holds_is_refused_before_anything_is_provisioned(
        OwnerLoginConflict conflict,
        string code)
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var bus = new StubMessageBus();
        var directory = new StubOwnerLoginDirectory(conflict);
        var handler = AccountHandlerFactory.Create(
            dbContext, FakeCurrentUser.Admin(), agent, bus: bus, ownerLogins: directory);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal([("acme", "owner@example.com")], directory.Asked);
        Assert.Empty(agent.Calls);
        Assert.Empty(await dbContext.Accounts.ToListAsync());
        Assert.Empty(bus.Invoked);
    }

    /// <summary>The inverse control: with no clash the same command creates the account.</summary>
    [Fact]
    public async Task An_owner_login_nobody_holds_creates_the_account()
    {
        await using var dbContext = CreateDbContext();
        var directory = new StubOwnerLoginDirectory();
        var handler = AccountHandlerFactory.Create(
            dbContext, FakeCurrentUser.Admin(), new RecordingAgentAccountsClient(), ownerLogins: directory);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", IpAddress: Ip, UserAgent: Client),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(directory.Asked);
        Assert.Single(await dbContext.Accounts.ToListAsync());
    }

    /// <summary>Seeds one plan and returns its id.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="diskQuotaMb">The plan's disk allowance.</param>
    /// <returns>The seeded plan's id.</returns>
    private static async Task<Guid> SeedPlanAsync(AccountsDbContext dbContext, int diskQuotaMb = 1_024)
    {
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", diskQuotaMb, 5, 2, 3, 5, 5);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan.Id;
    }

    /// <summary>The owner's language is stored on the account and carried to the login's invitation.</summary>
    [Fact]
    public async Task The_owners_language_is_stored_on_the_account_and_carried_to_the_logins_invitation()
    {
        await using var dbContext = CreateDbContext();
        var agent = new RecordingAgentAccountsClient();
        var bus = new StubMessageBus();
        var handler = AccountHandlerFactory.Create(dbContext, FakeCurrentUser.Admin(), agent, bus: bus);
        var planId = await SeedPlanAsync(dbContext);

        var result = await handler.HandleAsync(
            new CreateAccountCommand("acme", "acme.example.com", planId, "owner@example.com", "hy", IpAddress: Ip, UserAgent: Client),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hy", (await dbContext.Accounts.SingleAsync()).OwnerLanguage);
        var created = Assert.IsType<AccountCreated>(Assert.Single(bus.Invoked));
        Assert.Equal("hy", created.OwnerLanguage);
    }
}
