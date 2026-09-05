using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// What a deletion does when the cascade finishes quietly and the panel is still holding the
/// account's rows. It refuses, at the point where refusing is still free.
/// </summary>
/// <remarks>
/// <para>
/// This is the defect's own shape, and it is not the one the older tests cover. Those exercise a
/// subscriber that THROWS, which the handler has always caught. What had never been exercised is a
/// subscriber that does not exist: an unhandled event raises nothing, so the handler carried on, the
/// agent removed the system user, the account row went, and the task reported COMPLETED at 100 over
/// a <c>Site</c> row the panel then rendered as ENABLED for an account it no longer had.
/// </para>
/// <para>
/// The audit is what turns that silence into a refusal, so these tests assert on the refusal AND on
/// its timing: the agent must not have been asked. A deletion that discovered the residue after
/// <c>userdel</c> would be reporting a problem it had already made unrecoverable.
/// </para>
/// </remarks>
public sealed class DeleteAccountResidueTests : IDisposable
{
    /// <summary>The address every command in these tests is issued from.</summary>
    private const string Ip = "203.0.113.7";

    /// <summary>The user agent every command in these tests is issued with.</summary>
    private const string Client = "unit-tests";

    /// <summary>The instant seeded accounts are created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The Accounts context under test.</summary>
    private readonly AccountsDbContext _context = CreateDbContext();

    /// <summary>The journal the handler writes audit entries to.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>What the handler recorded as tasks.</summary>
    private readonly RecordingTaskRecorder _tasks = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A cascade that left rows behind stops the deletion before the host is touched.</summary>
    [Fact]
    public async Task A_cascade_that_left_rows_behind_stops_the_deletion_before_the_host_is_touched()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var auditor = new StubAccountResidueAuditor { Residue = ["Site(1)", "Certificate(1)"] };

        var result = await DeleteAsync(agent, auditor, account.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountCleanupFailed", result.Error!.Code);

        // The ORDER, which is the whole of the safety: the agent was never asked, so the system
        // user, the home directory and the customer's files are all still there and the account can
        // be deleted again once whatever kept its rows is fixed.
        Assert.Empty(agent.Calls);
        Assert.Single(await _context.Accounts.Where(row => row.Id == account.Id).ToListAsync());

        // And the task says so. A deletion that refused must not leave a task an operator reads as
        // finished, which is the half of this defect that made it survive a plan.
        var task = Assert.Single(_tasks.Tasks);
        Assert.False(task.Completed);
        Assert.Equal(result.Error!.Code, task.FailureCode);
    }

    /// <summary>A cascade that emptied every module lets the deletion complete.</summary>
    /// <remarks>
    /// The inverse control. A guard mutated to refuse everything passes every test that only ever
    /// hands it a reason to refuse, so this hands it none and requires the deletion through —
    /// including the audit having actually been consulted about THIS account, rather than the
    /// deletion having simply not called it.
    /// </remarks>
    [Fact]
    public async Task A_cascade_that_emptied_every_module_lets_the_deletion_complete()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var auditor = new StubAccountResidueAuditor();

        var result = await DeleteAsync(agent, auditor, account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(account.Id, auditor.Audited);
        Assert.Equal(["delete:acme"], agent.Calls);
        Assert.True(Assert.Single(_tasks.Tasks).Completed);
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

    /// <summary>Runs one deletion against the given audit.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="auditor">What the post-cascade audit reports.</param>
    /// <param name="accountId">The account to delete.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<ulong>> DeleteAsync(
        RecordingAgentAccountsClient agent,
        IAccountResidueAuditor auditor,
        Guid accountId)
    {
        var handler = new DeleteAccountCommandHandler(
            _context,
            agent,
            new StubMessageBus(),
            NullLogger<DeleteAccountCommandHandler>.Instance,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _tasks,
            auditor,
            new StubCorrelationIdAccessor(null));

        return handler.HandleAsync(new DeleteAccountCommand(accountId, Ip, Client), CancellationToken.None);
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
}
