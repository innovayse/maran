using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.ServerMigration.Commands.ControlMigrationAccount;

/// <summary>
/// Handles <see cref="ControlMigrationAccountCommand"/>: the per-account controls of a running job (issue #197,
/// phase 2).
/// </summary>
/// <remarks>
/// The cut-over is the administrator's decision and runs outside the concurrency slots: it is short next to the
/// initial sync and the administrator has already quiesced the source. A cut-over, like a retry, needs a job that is
/// running; a paused job holds every account where it is.
/// </remarks>
public sealed class ControlMigrationAccountCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Ends accounts the administrator cancels.</summary>
    private readonly MigrationAccountCanceller _canceller;

    /// <summary>Resolves a failure code to its sentence.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Sends the messages that run a pass.</summary>
    private readonly IMessageBus _bus;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="canceller">Ends accounts the administrator cancels.</param>
    /// <param name="failureNames">Resolves a failure code to its sentence.</param>
    /// <param name="bus">Sends the messages that run a pass.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ControlMigrationAccountCommandHandler(
        ServerMigrationDbContext dbContext,
        MigrationAccountCanceller canceller,
        AccountImportFailureNames failureNames,
        IMessageBus bus,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _canceller = canceller;
        _failureNames = failureNames;
        _bus = bus;
        _journal = journal;
        _names = names;
    }

    /// <summary>Does what was asked.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The account after the change; <c>MigrationJobNotFound</c>, <c>MigrationJobAccountNotFound</c> or a refusal of the state otherwise.</returns>
    public async Task<Result<MigrationJobAccountDto>> HandleAsync(
        ControlMigrationAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await _dbContext.MigrationJobs.FirstOrDefaultAsync(row => row.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result<MigrationJobAccountDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var account = await _dbContext.FindAccountAsync(job.Id, command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<MigrationJobAccountDto>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationJobAccountNotFound), ErrorType.NotFound));
        }

        if (command.Action != MigrationAccountAction.Cancel && !job.IsRunning)
        {
            return Result<MigrationJobAccountDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobStateInvalid), ErrorType.Conflict));
        }

        var changed = command.Action switch
        {
            MigrationAccountAction.Retry => account.Retry(),
            MigrationAccountAction.CutOver => account.BeginCutOver(),
            _ => account.HasEnded
                ? Result.Fail(Error.Of(nameof(ErrorMessages.MigrationAccountStateInvalid), ErrorType.Conflict))
                : await _canceller.CancelAsync(account, cancellationToken),
        };
        if (!changed.IsSuccess)
        {
            return Result<MigrationJobAccountDto>.Fail(changed.Error!);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            command.Action == MigrationAccountAction.Cancel ? MigrationAuditActions.AccountCancelled : MigrationAuditActions.AccountRetried,
            account.SourceAccountName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        if (command.Action == MigrationAccountAction.CutOver)
        {
            await _bus.PublishAsync(new MigrationAccountTransferRequested(account.Id, MigrationRunKind.CutOver));
        }
        else
        {
            await _bus.PublishAsync(new MigrationJobTickRequested(job.Id));
        }

        return Result<MigrationJobAccountDto>.Ok(
            MigrationJobMapper.ToDto(account, _failureNames.Of(account.FailureCode), _names));
    }
}
