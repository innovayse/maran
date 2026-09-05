using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// What a finished deletion tells the operator about the modules its audit could not read. It names
/// them, because a module that was never asked has not been found clean.
/// </summary>
/// <remarks>
/// <para>
/// This is the reporting half of the defect, one level up from the leak itself. The cascade's own
/// silence was mistaken for success once already — two modules subscribed to nothing, the publisher
/// saw no exception, and the task said COMPLETED at 100 over rows the panel went on rendering. The
/// audit closed that. But the audit skips a module it cannot read, deliberately, so that its own
/// outage cannot make an account undeletable — and a skip that only reached a log line would put the
/// panel back where it started: a completion claiming more than anybody looked at.
/// </para>
/// <para>
/// So these tests are on the CLAIM rather than on the deletion: the deletion goes through either
/// way, and the difference that has to be visible is what it says it checked.
/// </para>
/// </remarks>
public sealed class DeleteAccountUncheckedModuleTests : IDisposable
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

    /// <summary>A deletion whose audit could not read a module names it on the task.</summary>
    [Fact]
    public async Task A_deletion_whose_audit_could_not_read_a_module_names_it_on_the_task()
    {
        var account = await SeedAsync();
        var auditor = new StubAccountResidueAuditor { Unchecked = ["SitesDbContext"] };

        var result = await DeleteAsync(auditor, account.Id);

        // The deletion itself is not the thing under test and must still go through: an audit that
        // vetoed a deletion by failing would trade a reporting defect for an unremovable account.
        Assert.True(result.IsSuccess, result.Error?.Code);

        var task = Assert.Single(_tasks.Tasks);
        var line = Assert.Single(task.Reports.Where(report =>
        {
            return report.Line.Contains("SitesDbContext", StringComparison.Ordinal);
        }).ToList());

        // Named AND qualified: the module's name alone could be a line saying it was cleaned.
        Assert.Contains("NOT be checked", line.Line, StringComparison.Ordinal);
    }

    /// <summary>A deletion whose audit read every module claims nothing about modules it skipped.</summary>
    /// <remarks>
    /// The inverse control. A handler that appended the qualification unconditionally — or one that
    /// printed the whole audit object — would satisfy the test above while telling an operator every
    /// deletion is partly unchecked, which is a warning that gets learned and then ignored. So this
    /// one hands the audit nothing to qualify and requires the claim to come out unqualified, while
    /// still stating that something was looked at.
    /// </remarks>
    [Fact]
    public async Task A_deletion_whose_audit_read_every_module_claims_nothing_about_modules_it_skipped()
    {
        var account = await SeedAsync();

        var result = await DeleteAsync(new StubAccountResidueAuditor(), account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var task = Assert.Single(_tasks.Tasks);
        Assert.DoesNotContain(task.Reports, report =>
        {
            return report.Line.Contains("NOT be checked", StringComparison.Ordinal);
        });
        Assert.Contains(task.Reports, report =>
        {
            return report.Line.Contains("nothing names this account", StringComparison.Ordinal);
        });
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
    /// <param name="auditor">What the post-cascade audit reports.</param>
    /// <param name="accountId">The account to delete.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<ulong>> DeleteAsync(StubAccountResidueAuditor auditor, Guid accountId)
    {
        var handler = new DeleteAccountCommandHandler(
            _context,
            new RecordingAgentAccountsClient(),
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
