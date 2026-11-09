using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.SaveBackupSchedule;

/// <summary>
/// Handles <see cref="SaveBackupScheduleCommand"/>: creates the account's schedule or replaces the
/// one it already has.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row is created or reconfigured by <see cref="BackupScheduleWriter"/>, the one place that
/// keeps a schedule single.</b> The administrator's surface and the customer's surface share it, so
/// the lookup that holds the host-wide row to one (two host-wide schedules would mean two backups of
/// every account a night) is written once.
/// </para>
/// <para>
/// <b>A named account is resolved through the tenant-scoped directory even though the surface is
/// administrators-only.</b> The point is not authorization — the controller's policy has already
/// settled that — it is that a schedule for an account that does not exist is a row nothing will
/// ever run, and finding that out at save time is the difference between a form that refuses and a
/// backup an operator believes is happening.
/// </para>
/// <para>
/// No tenant filter is bypassed anywhere here. The caller is an administrator, so the module's own
/// query filter admits every row already, and reaching for <c>IgnoreQueryFilters</c> in a request
/// path would be turning a policy decision into a silent one.
/// </para>
/// </remarks>
public sealed class SaveBackupScheduleCommandHandler
{
    /// <summary>The one window onto whether the named account exists.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The one place a schedule row is created or reconfigured.</summary>
    private readonly BackupScheduleWriter _writer;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The window onto whether the named account exists.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="writer">The one place a schedule row is created or reconfigured.</param>
    public SaveBackupScheduleCommandHandler(
        IAccountDirectory accounts,
        BackupAuditJournal journal,
        BackupScheduleWriter writer)
    {
        _accounts = accounts;
        _journal = journal;
        _writer = writer;
    }

    /// <summary>Saves one schedule and answers with what was stored.</summary>
    /// <param name="command">The validated schedule; see <see cref="SaveBackupScheduleCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored schedule, or <c>AccountNotFound</c> when it names an account that is not there.</returns>
    public async Task<Result<BackupScheduleDto>> HandleAsync(
        SaveBackupScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The journal's subject: the account whose backups the schedule governs, as an operator
        // would search for it, or empty for the server-wide default — that schedule acts on no one
        // account, and the action name already says what was saved (the same "otherwise empty"
        // convention the Identity journal documents).
        var subject = string.Empty;
        if (command.AccountId is not null)
        {
            var account = await _accounts.FindAsync(command.AccountId.Value, cancellationToken);
            if (account is null)
            {
                // No account can be named for this caller, so the trace records the identifier
                // that was probed for.
                await _journal.RecordFailureAsync(
                    BackupAuditActions.BackupScheduleSaved,
                    command.AccountId.Value.ToString(),
                    command.IpAddress,
                    command.UserAgent,
                    cancellationToken);

                return Result<BackupScheduleDto>.Fail(
                    Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
            }

            subject = account.Username;
        }

        var cadence = BackupCadenceTranslator.From(command.Frequency, command.HourUtc, command.DayOfWeekUtc);
        if (cadence.Error is { } refusal)
        {
            return Result<BackupScheduleDto>.Fail(refusal);
        }

        var schedule = await _writer.SaveAsync(
            command.AccountId,
            command.DestinationId,
            cadence.Value,
            command.RetainCount,
            command.Enabled,
            cancellationToken);

        await _journal.RecordSuccessAsync(
            BackupAuditActions.BackupScheduleSaved,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<BackupScheduleDto>.Ok(BackupScheduleMapper.From(schedule));
    }
}
