using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// The final backup spec §12 promises: that it is taken before anything is released, that a failure
/// refuses the deletion outright, and that the two ways of proceeding without one are told apart.
/// </summary>
/// <remarks>
/// The question these are written from is the asymmetric one. Proceeding after a failed backup
/// destroys the last copy of a customer's data, silently, in the operation that has no undo;
/// refusing costs a retry. So the refusal is the behaviour under test, and its cost — an account
/// that cannot be deleted — is bounded by the two skip paths, which are tested beside it because a
/// refusal with no exit is not a defensible design.
/// </remarks>
public sealed class DeleteAccountFinalBackupTests : IDisposable
{
    /// <summary>The address every command in these tests is issued from.</summary>
    private const string Ip = "203.0.113.11";

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

    /// <summary>A failed final backup refuses the deletion and the account survives untouched.</summary>
    /// <remarks>
    /// The whole argument of this step, pinned. The agent must not have been asked and the row must
    /// still be there, because refusing at this point is refusing before anything at all has been
    /// destroyed — which is what makes refusing the cheap answer and proceeding the permanent one.
    /// </remarks>
    [Fact]
    public async Task A_failed_final_backup_refuses_the_deletion_and_the_account_survives()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var backups = StubAccountBackupService.Failing();

        var result = await DeleteAsync(agent, backups, account.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("FinalBackupFailed", result.Error!.Code);

        Assert.Empty(agent.Calls);
        Assert.Single(await _context.Accounts.Where(row => row.Id == account.Id).ToListAsync());

        var task = Assert.Single(_tasks.Tasks);
        Assert.False(task.Completed);
        Assert.Equal("FinalBackupFailed", task.FailureCode);
    }

    /// <summary>A successful final backup lets the deletion through, and is taken before the cascade.</summary>
    /// <remarks>
    /// The inverse control. A step mutated to refuse everything would pass every test that only
    /// hands it a failing backup, so this hands it a succeeding one and requires the deletion
    /// through — and asserts the backup was taken of the right account, by name.
    /// </remarks>
    [Fact]
    public async Task A_successful_final_backup_lets_the_deletion_complete()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var backups = new StubAccountBackupService();

        var result = await DeleteAsync(agent, backups, account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal((account.Id, "acme"), Assert.Single(backups.Taken));
        Assert.Equal(["delete:acme"], agent.Calls);
        Assert.True(Assert.Single(_tasks.Tasks).Completed);
    }

    /// <summary>With no Backups module composed the deletion proceeds, and says so in its own entry.</summary>
    [Fact]
    public async Task With_no_backups_module_the_deletion_proceeds_and_audits_that_it_did()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();

        var result = await DeleteAsync(agent, backups: null, account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Contains(_audit.Entries, entry =>
        {
            return entry.Action == AuditActions.FinalBackupSkippedNoModule && entry.Succeeded;
        });
    }

    /// <summary>An administrator may skip the backup, and the journal records who did.</summary>
    [Fact]
    public async Task The_skip_flag_proceeds_without_a_backup_and_audits_the_caller()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var backups = StubAccountBackupService.Failing();

        var result = await DeleteAsync(agent, backups, account.Id, skipFinalBackup: true);

        // Skipped means the failing service was never even asked, which is the point: an account
        // whose data can never be archived is deletable only this way.
        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Empty(backups.Taken);

        var entry = Assert.Single(_audit.Entries, candidate =>
        {
            return candidate.Action == AuditActions.FinalBackupSkipped;
        });
        Assert.Equal(Ip, entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The two ways of proceeding without a backup are journalled under different actions.</summary>
    /// <remarks>
    /// "Nobody chose this" and "an administrator chose this" are different facts about why no copy
    /// exists. One action for both would make them the same observation — which is the substitution
    /// this whole cascade's history is a record of.
    /// </remarks>
    [Fact]
    public async Task The_two_ways_of_skipping_are_never_recorded_under_one_action()
    {
        var withoutModule = await SeedAsync();
        await DeleteAsync(new RecordingAgentAccountsClient(), backups: null, withoutModule.Id);

        var byChoice = await SeedAsync("beta", "beta.example.com");
        await DeleteAsync(
            new RecordingAgentAccountsClient(), new StubAccountBackupService(), byChoice.Id, skipFinalBackup: true);

        Assert.Single(_audit.Entries, entry =>
        {
            return entry.Action == AuditActions.FinalBackupSkippedNoModule;
        });
        Assert.Single(_audit.Entries, entry =>
        {
            return entry.Action == AuditActions.FinalBackupSkipped;
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

    /// <summary>Runs one deletion with the given final-backup arrangement.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="backups">The final-backup service, or null for a panel without the module.</param>
    /// <param name="accountId">The account to delete.</param>
    /// <param name="skipFinalBackup">Whether the caller asked for the backup to be skipped.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<ulong>> DeleteAsync(
        RecordingAgentAccountsClient agent,
        IAccountBackupService? backups,
        Guid accountId,
        bool skipFinalBackup = false)
    {
        var handler = new DeleteAccountCommandHandler(
            _context,
            agent,
            new StubMessageBus(),
            NullLogger<DeleteAccountCommandHandler>.Instance,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _tasks,
            new StubAccountResidueAuditor(),
            new StubCorrelationIdAccessor(null),
            backups is null ? [] : [backups]);

        return handler.HandleAsync(
            new DeleteAccountCommand(accountId, Ip, Client, skipFinalBackup), CancellationToken.None);
    }

    /// <summary>Seeds one account to delete.</summary>
    /// <param name="name">The account's system user name.</param>
    /// <param name="domain">The account's primary domain.</param>
    /// <returns>The seeded account.</returns>
    private async Task<Account> SeedAsync(string name = "acme", string domain = "acme.example.com")
    {
        var account = new Account(Guid.NewGuid(), name, domain, Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }
}
