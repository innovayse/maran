using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Npgsql;

namespace Maran.Modules.Backups.IntegrationEvents.Handlers;

/// <summary>
/// Records, when an account is created, that its creator asked for it to be left out of scheduled backups
/// (<see cref="AccountCreated.BackupEnabled"/> false). An account created the ordinary way gets no row at
/// all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent, and backed by the key rather than by timing.</b> The creation event is invoked once per
/// created account but can be announced again by a retry; the row's primary key is the account id, so a
/// second delivery collides on it. The collision is caught and treated as "already recorded". There is no
/// read before the insert, deliberately: the module's tenant filter shows a reseller or an anonymous
/// caller nothing, so a pre-check run as the creating login would always answer "no row yet" and the key
/// would still be the only thing that stopped the duplicate. Relying on the key alone says that once.
/// </para>
/// <para>
/// <b>It never fails the account.</b> The account row and its system user already exist when this runs,
/// and the choice it records only affects an unattended sweep later. A failure to write it (apart from the
/// duplicate above) propagates to the Accounts handler, which reports it as a failure to issue the owner's
/// login and names the recovery — see <see cref="AccountCreated"/> — and the absent row means the account
/// is backed up, the safe direction (<see cref="AccountBackupOptOut"/>).
/// </para>
/// <para>
/// <b>One journal line</b> records the exclusion (<see cref="BackupAuditActions.BackupOptedOut"/>), written
/// as the system after the row is stored, so the audit trail can answer "why does this account have no
/// scheduled backups" without reading the creation request.
/// </para>
/// </remarks>
public sealed class AccountCreatedHandler
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The panel's clock.</param>
    public AccountCreatedHandler(BackupsDbContext dbContext, BackupAuditJournal journal, IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Stores the opt-out row when the account was created without backups, and nothing otherwise.</summary>
    /// <param name="message">The account that was just created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the row is stored (or was already), or at once when the account is backed up.</returns>
    public async Task HandleAsync(AccountCreated message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.BackupEnabled)
        {
            return;
        }

        var optOut = new AccountBackupOptOut(message.AccountId, _clock.UtcNow);
        _dbContext.AccountBackupOptOuts.Add(optOut);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A repeat: the row for this account is already there. Detached rather than left tracked as a
            // pending add on a context that may be reused, and no second journal line is written.
            _dbContext.Entry(optOut).State = EntityState.Detached;
            return;
        }

        await _journal.RecordSystemAsync(
            BackupAuditActions.BackupOptedOut, message.Username, succeeded: true, cancellationToken);
    }
}
