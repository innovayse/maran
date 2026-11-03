using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// The fixture the suspension and reactivation tests share: one in-memory Accounts context, the
/// agent and task doubles, and the three things every such test does — seed an account, suspend it,
/// reactivate it. Held in one place so the test classes, which are split by what they assert, do not
/// each carry a copy.
/// </summary>
public sealed class AccountSuspensionHarness : IDisposable
{
    /// <summary>The instant seeded accounts are created at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The caller address every command run through the harness carries.</summary>
    public const string Ip = "203.0.113.7";

    /// <summary>The user agent every command run through the harness carries.</summary>
    public const string Client = "unit-tests";

    /// <summary>The Accounts context under test.</summary>
    public AccountsDbContext Context { get; } = CreateDbContext();

    /// <summary>The agent double both handlers drive.</summary>
    public RecordingAgentAccountsClient Agent { get; } = new();

    /// <summary>What the handlers recorded as tasks; asserted on by the task-shaped tests.</summary>
    public RecordingTaskRecorder Tasks { get; } = new();

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Builds a journal writing into a writer nothing asserts on; the audit tests do that.</summary>
    /// <returns>The journal.</returns>
    private static AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(new RecordingAuditWriter(), FakeCurrentUser.Admin());
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        Context.Dispose();
    }

    /// <summary>Seeds one active account named "acme".</summary>
    /// <returns>The seeded account.</returns>
    public async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one suspension against the harness's context.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to suspend.</param>
    /// <param name="logger">
    /// Where the handler writes the reason it refused, when a test asserts that sentence; the null
    /// logger otherwise, which is every test whose subject is the outcome rather than the wording.
    /// </param>
    /// <returns>The handler's result.</returns>
    public Task<Result<AccountDto>> SuspendAsync(
        IAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId,
        ILogger<SuspendAccountCommandHandler>? logger = null)
    {
        var handler = new SuspendAccountCommandHandler(
            Context,
            agent,
            bus,
            logger ?? NullLogger<SuspendAccountCommandHandler>.Instance,
            Journal(),
            Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            new StubDisplayNames());

        return handler.HandleAsync(new SuspendAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }

    /// <summary>Runs one reactivation against the harness's context.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to reactivate.</param>
    /// <param name="logger">
    /// Where the handler writes the reason it refused, when a test asserts that sentence; the null
    /// logger otherwise.
    /// </param>
    /// <returns>The handler's result.</returns>
    public Task<Result<AccountDto>> ReactivateAsync(
        IAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId,
        ILogger<ReactivateAccountCommandHandler>? logger = null)
    {
        var handler = new ReactivateAccountCommandHandler(
            Context,
            agent,
            bus,
            logger ?? NullLogger<ReactivateAccountCommandHandler>.Instance,
            Journal(),
            Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            new StubDisplayNames());

        return handler.HandleAsync(new ReactivateAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }
}
