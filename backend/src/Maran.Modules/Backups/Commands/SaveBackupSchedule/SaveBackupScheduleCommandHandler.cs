using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
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
/// <b>The read before the insert is what keeps a schedule single, and for the host-wide row it is
/// the only thing that does.</b> The per-account rows are held apart by a unique index; the
/// host-wide row names no account and PostgreSQL counts NULLs as distinct, so the index cannot
/// cover it on every supported platform (<c>BackupScheduleConfiguration</c> says why). Two
/// host-wide schedules would mean two backups of every account a night, which is the failure this
/// lookup exists against.
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
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto whether the named account exists.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The window onto whether the named account exists.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp an enabled schedule.</param>
    public SaveBackupScheduleCommandHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        BackupAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _journal = journal;
        _clock = clock;
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
                    AuditActions.BackupScheduleSaved,
                    command.AccountId.Value.ToString(),
                    command.IpAddress,
                    command.UserAgent,
                    cancellationToken);

                return Result<BackupScheduleDto>.Fail(
                    Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
            }

            subject = account.Username;
        }

        var schedule = await _dbContext.BackupSchedules
            .FirstOrDefaultAsync(row => row.AccountId == command.AccountId, cancellationToken);

        if (schedule is null)
        {
            schedule = new BackupSchedule(
                Guid.NewGuid(),
                command.AccountId,
                command.DestinationId,
                command.Frequency,
                command.HourUtc,
                command.DayOfWeekUtc,
                command.RetainCount);

            _dbContext.BackupSchedules.Add(schedule);
        }

        // Reconfigure runs for a new schedule too, because a new schedule is created disabled and it
        // is this call that switches it on — including the stamp that stops the very first run from
        // happening the moment the form is saved.
        schedule.Reconfigure(
            command.DestinationId,
            command.Frequency,
            command.HourUtc,
            command.DayOfWeekUtc,
            command.RetainCount,
            command.Enabled,
            _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.BackupScheduleSaved,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<BackupScheduleDto>.Ok(BackupScheduleMapper.From(schedule));
    }
}
