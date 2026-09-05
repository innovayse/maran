using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// What deleting an account leaves in the panel's task journal. Deletion is the longest and most
/// destructive operation the panel offers, so it is the one an operator most needs to watch rather
/// than guess at — and the record it leaves has to agree with the answer the caller got.
/// </summary>
public sealed class DeleteAccountTaskRecordingTests : IDisposable
{
    /// <summary>The address every command in these tests is issued from.</summary>
    private const string Ip = "203.0.113.7";

    /// <summary>The user agent every command in these tests is issued with.</summary>
    private const string Client = "unit-tests";

    /// <summary>The correlation id of the request the deletions in these tests belong to.</summary>
    private const string Correlation = "corr-42";

    /// <summary>The instant seeded accounts are created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The Accounts context under test.</summary>
    private readonly AccountsDbContext _context = CreateDbContext();

    /// <summary>The journal the handler writes audit entries to; unused here beyond satisfying it.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>What the handler recorded as tasks.</summary>
    private readonly RecordingTaskRecorder _tasks = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A completed deletion leaves exactly one task naming the account and closed as finished.</summary>
    [Fact]
    public async Task A_completed_deletion_leaves_exactly_one_task_naming_the_account_and_closed_as_finished()
    {
        var account = await SeedAsync();

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(_tasks.Tasks);
        Assert.Equal(TaskKinds.AccountDeletion, task.Kind);
        Assert.Equal("acme", task.Subject);
        Assert.Equal(Correlation, task.CorrelationId);
        Assert.True(task.Completed);
        Assert.Null(task.FailureCode);
    }

    /// <summary>A deletion a module refused leaves one task carrying the same error the response did.</summary>
    /// <remarks>
    /// The agreement is the whole point. A task closed under a different code — or left open — makes
    /// the pane and the response two accounts of one event, and the operator has no way to tell which
    /// is the true one.
    /// </remarks>
    [Fact]
    public async Task A_deletion_a_module_refused_leaves_one_task_carrying_the_same_error_the_response_did()
    {
        var account = await SeedAsync();
        var bus = new StubMessageBus(new InvalidOperationException("a module still holds rows"));

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), bus, account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.Equal("AccountCleanupFailed", result.Error!.Code);
        Assert.Equal(result.Error!.Code, task.FailureCode);
        Assert.False(task.Completed);
    }

    /// <summary>A deletion the agent refused leaves one task carrying the same error the response did.</summary>
    [Fact]
    public async Task A_deletion_the_agent_refused_leaves_one_task_carrying_the_same_error_the_response_did()
    {
        // The other failure path, and the one that matters most: the cascade has already destroyed
        // things by the time the agent refuses, so this task is the operator's record that a
        // deletion got part-way and stopped.
        var account = await SeedAsync();

        var result = await DeleteAsync(
            new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure", ErrorType.Failure)), new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(result.Error!.Code, task.FailureCode);
    }

    /// <summary>A deletion of an unknown account leaves no task at all.</summary>
    /// <remarks>
    /// Nothing ran and nothing was destroyed, so there is nothing to watch. The probe is still
    /// journalled — that is what the audit journal is for — but a task naming a raw identifier that
    /// never existed is a row an operator can only be confused by.
    /// </remarks>
    [Fact]
    public async Task A_deletion_of_an_unknown_account_leaves_no_task_at_all()
    {
        var result = await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), Guid.NewGuid());

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(_tasks.Tasks);
    }

    /// <summary>A deletion reports its stages in order and never goes backwards.</summary>
    [Fact]
    public async Task A_deletion_reports_its_stages_in_order_and_never_goes_backwards()
    {
        // Instrumentation that reports out of order is worse than none: a pane that jumps backwards
        // reads as an operation restarting, on the one operation nobody wants to see restart.
        var account = await SeedAsync();

        await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        var percentages = task.Reports.Select(report =>
        {
            return report.Percent;
        }).ToList();

        Assert.NotEmpty(percentages);
        Assert.Equal(percentages.Order(), percentages);
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Seeds one active account named "acme".</summary>
    /// <returns>The seeded account.</returns>
    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one deletion.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to delete.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<ulong>> DeleteAsync(
        RecordingAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId)
    {
        var handler = new DeleteAccountCommandHandler(
            _context,
            agent,
            bus,
            NullLogger<DeleteAccountCommandHandler>.Instance,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _tasks,
            new StubAccountResidueAuditor(),
            new StubCorrelationIdAccessor(Correlation));

        return handler.HandleAsync(new DeleteAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }
}
