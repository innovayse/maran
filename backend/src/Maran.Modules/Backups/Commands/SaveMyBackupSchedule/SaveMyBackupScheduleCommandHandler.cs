using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.SaveMyBackupSchedule;

/// <summary>
/// Handles <see cref="SaveMyBackupScheduleCommand"/>: creates the caller's own schedule or replaces the
/// one they already have (issue #147).
/// </summary>
/// <remarks>
/// <para>
/// <b>The account is resolved through the tenant-scoped directory, and an account the caller does not
/// own answers exactly as one that does not exist.</b> The command's account member is stamped by the
/// action from the signed-in caller, so in practice it is always the caller's own; the lookup is what
/// keeps that true if the command is ever dispatched from elsewhere, and it is what makes an
/// administrator or an account-less login (who have no account of their own) answer 404 and not write
/// a row. A schedule with no account is the host-wide policy, and this handler cannot produce one: the
/// account it writes is the one the directory returned.
/// </para>
/// <para>
/// <b>It writes the same row an administrator's per-account override uses</b> (one row, one meaning),
/// through <see cref="BackupScheduleWriter"/>, and leaves the destination that row names alone. The
/// audit entry names who last wrote it.
/// </para>
/// </remarks>
public sealed class SaveMyBackupScheduleCommandHandler
{
    /// <summary>The one window onto whether the caller's account exists.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The one place a schedule row is created or reconfigured.</summary>
    private readonly BackupScheduleWriter _writer;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The window onto whether the caller's account exists.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="writer">The one place a schedule row is created or reconfigured.</param>
    public SaveMyBackupScheduleCommandHandler(
        IAccountDirectory accounts,
        BackupAuditJournal journal,
        BackupScheduleWriter writer)
    {
        _accounts = accounts;
        _journal = journal;
        _writer = writer;
    }

    /// <summary>Saves the caller's schedule and answers with what was stored.</summary>
    /// <param name="command">The validated schedule; see <see cref="SaveMyBackupScheduleCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored schedule, or <c>AccountNotFound</c> when the caller has no account of their own.</returns>
    public async Task<Result<BackupScheduleDto>> HandleAsync(
        SaveMyBackupScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            await _journal.RecordFailureAsync(
                BackupAuditActions.BackupScheduleSaved,
                command.AccountId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<BackupScheduleDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var cadence = BackupCadenceTranslator.From(command.Frequency, command.HourUtc, command.DayOfWeekUtc);
        if (cadence.Error is { } refusal)
        {
            return Result<BackupScheduleDto>.Fail(refusal);
        }

        // The account written is the directory's answer, not the command's field: the row can only
        // ever belong to an account this caller is allowed to see.
        var schedule = await _writer.SaveKeepingDestinationAsync(
            account.Id,
            cadence.Value,
            command.RetainCount,
            command.Enabled,
            cancellationToken);

        await _journal.RecordSuccessAsync(
            BackupAuditActions.BackupScheduleSaved,
            account.Username,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<BackupScheduleDto>.Ok(BackupScheduleMapper.From(schedule));
    }
}
