using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// The fixture the reseller lifecycle tests share: ONE in-memory Accounts database opened twice —
/// once as an administrator to seed (which no filter narrows) and once as the caller under test —
/// so a test can plant a foreign reseller's account and prove the caller's context cannot see it.
/// </summary>
public sealed class ResellerAccountLifecycleHarness : IDisposable
{
    /// <summary>The caller address every command run through the harness carries.</summary>
    public const string Ip = "203.0.113.9";

    /// <summary>The user agent every command run through the harness carries.</summary>
    public const string Client = "unit-tests";

    /// <summary>The database both contexts open.</summary>
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>The principal the handlers run as.</summary>
    private readonly ICurrentUser _caller;

    /// <summary>The context used to seed rows, never narrowed.</summary>
    public AccountsDbContext Seeder { get; }

    /// <summary>The context the handlers use, narrowed for the caller.</summary>
    public AccountsDbContext Context { get; }

    /// <summary>The audit writer the handlers' journal writes to.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The agent double the handlers drive.</summary>
    public RecordingAgentAccountsClient Agent { get; } = new();

    /// <summary>What the handlers recorded as tasks.</summary>
    public RecordingTaskRecorder Tasks { get; } = new();

    /// <summary>The bus the cascades are invoked on.</summary>
    public StubMessageBus Bus { get; } = new();

    /// <summary>Creates the fixture for one caller.</summary>
    /// <param name="caller">The principal the handlers run as.</param>
    public ResellerAccountLifecycleHarness(ICurrentUser caller)
    {
        _caller = caller;
        Seeder = AccountsTestContext.CreateOn(_databaseName, FakeCurrentUser.Admin());
        Context = AccountsTestContext.CreateOn(_databaseName, caller);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        Seeder.Dispose();
        Context.Dispose();
    }

    /// <summary>Seeds a reseller.</summary>
    /// <param name="id">The reseller's identity.</param>
    /// <param name="name">The reseller's name.</param>
    /// <param name="suspended">Whether the reseller is suspended.</param>
    /// <returns>The reseller.</returns>
    public async Task<Reseller> SeedResellerAsync(Guid id, string name, bool suspended = false)
    {
        var reseller = ResellerTestData.Reseller(name, id: id);
        if (suspended)
        {
            reseller.Suspend();
        }

        Seeder.Resellers.Add(reseller);
        await Seeder.SaveChangesAsync();
        return reseller;
    }

    /// <summary>Seeds an account owned by a reseller.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="resellerId">The owning reseller, or null for the administrator.</param>
    /// <param name="suspended">Whether the account is seeded suspended.</param>
    /// <returns>The account.</returns>
    public async Task<Account> SeedAccountAsync(string name, Guid? resellerId, bool suspended = false)
    {
        var account = new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            Guid.NewGuid(),
            ResellerTestData.CreatedAt,
            resellerId: resellerId);
        if (suspended)
        {
            account.Suspend();
        }

        Seeder.Accounts.Add(account);
        await Seeder.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one suspension as the caller.</summary>
    /// <param name="accountId">The account to suspend.</param>
    /// <returns>The handler's result.</returns>
    public Task<Result<AccountDto>> SuspendAsync(Guid accountId)
    {
        var handler = new SuspendAccountCommandHandler(
            Context,
            new ResellerScope(Context, _caller),
            new ResellerNameDirectory(Context),
            Agent,
            Bus,
            NullLogger<SuspendAccountCommandHandler>.Instance,
            Journal(),
            Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            new StubDisplayNames());

        return handler.HandleAsync(new SuspendAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }

    /// <summary>Runs one reactivation as the caller.</summary>
    /// <param name="accountId">The account to reactivate.</param>
    /// <returns>The handler's result.</returns>
    public Task<Result<AccountDto>> ReactivateAsync(Guid accountId)
    {
        var handler = new ReactivateAccountCommandHandler(
            Context,
            new ResellerScope(Context, _caller),
            new ResellerNameDirectory(Context),
            Agent,
            Bus,
            NullLogger<ReactivateAccountCommandHandler>.Instance,
            Journal(),
            Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            new StubDisplayNames());

        return handler.HandleAsync(new ReactivateAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }

    /// <summary>Runs one deletion as the caller.</summary>
    /// <param name="accountId">The account to delete.</param>
    /// <param name="skipFinalBackup">Whether the command asks to skip the final backup.</param>
    /// <param name="backups">The final-backup service, or null for a panel with no Backups module.</param>
    /// <returns>The handler's result.</returns>
    public Task<Result<ulong>> DeleteAsync(
        Guid accountId,
        bool skipFinalBackup = false,
        IAccountBackupService? backups = null)
    {
        var handler = new DeleteAccountCommandHandler(
            Context,
            new ResellerScope(Context, _caller),
            Agent,
            Bus,
            NullLogger<DeleteAccountCommandHandler>.Instance,
            Journal(),
            Tasks,
            new StubAccountResidueAuditor(),
            new StubCorrelationIdAccessor(null),
            backups is null ? [] : [backups]);

        return handler.HandleAsync(
            new DeleteAccountCommand(accountId, Ip, Client, skipFinalBackup), CancellationToken.None);
    }

    /// <summary>Builds the journal over the fixture's recorder, as the caller.</summary>
    /// <returns>The journal.</returns>
    private AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(Audit, _caller);
    }
}
