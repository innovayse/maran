using Maran.Modules.Updates.Common;
using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Resources;
using Maran.Modules.Updates.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates.Commands.StartPanelUpdate;

/// <summary>
/// Handles <see cref="StartPanelUpdateCommand"/>: opens a task, journals the request and then hands the
/// root update service its marker (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the control.</b> The task and the ledger exist before the marker, so the update the
/// marker starts always has a task to report to. The audit entry is written BEFORE the marker, so an
/// update that started is never an update that was not journalled; if the marker then cannot be created
/// the journal gets a second, failed entry. A refusal that happens before any of that is journalled as a
/// failed request too.
/// </para>
/// <para>
/// <b>Nothing the caller sent is read.</b> The command holds only server-established members, and the
/// marker's content is a constant. The handler never reports a path: an IO failure becomes
/// <c>PanelUpdateUnavailable</c> and nothing about it.
/// </para>
/// <para>
/// <b>Why concurrent requests cannot both succeed.</b> The in-progress check is advice that spares a
/// task; the marker's exclusive creation is the control. Two simultaneous requests may both pass the
/// check, but only one creates the marker, and the other fails its task, removes its ledger and answers
/// <c>PanelUpdateRunning</c>. The root side independently refuses a second run through the panel archive
/// lock.
/// </para>
/// </remarks>
public sealed class StartPanelUpdateCommandHandler
{
    /// <summary>The first line of a new task, before root has picked the request up.</summary>
    private const string RequestedLine = "Update requested; waiting for the root update service to start.";

    /// <summary>What the task's progress is when it opens.</summary>
    private const int RequestedPercent = 5;

    /// <summary>Reads the panel's release state and whether an update is in flight.</summary>
    private readonly PanelUpdateStateReader _reader;

    /// <summary>Records the update as a panel task.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Carries the task's id across the restart the update causes.</summary>
    private readonly PanelUpdateTaskLedger _ledger;

    /// <summary>Hands the request to the root update service.</summary>
    private readonly PanelUpdateRequestMarker _marker;

    /// <summary>The module's audit journal.</summary>
    private readonly UpdatesAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="reader">Reads the panel's release state and whether an update is in flight.</param>
    /// <param name="tasks">Records the update as a panel task.</param>
    /// <param name="ledger">Carries the task's id across the restart.</param>
    /// <param name="marker">Hands the request to the root update service.</param>
    /// <param name="journal">The module's audit journal.</param>
    public StartPanelUpdateCommandHandler(
        PanelUpdateStateReader reader,
        ITaskRecorder tasks,
        PanelUpdateTaskLedger ledger,
        PanelUpdateRequestMarker marker,
        UpdatesAuditJournal journal)
    {
        _reader = reader;
        _tasks = tasks;
        _ledger = ledger;
        _marker = marker;
        _journal = journal;
    }

    /// <summary>Requests the update.</summary>
    /// <param name="command">The request; it holds only what the server established.</param>
    /// <param name="cancellationToken">Cancels the request before the marker is created.</param>
    /// <returns>The task that carries the update's progress, or the reason it was refused.</returns>
    public async Task<Result<PanelUpdateRequestedDto>> HandleAsync(
        StartPanelUpdateCommand command,
        CancellationToken cancellationToken)
    {
        var facts = _reader.Read();
        if (PanelUpdateLedgerPolicy.IsInProgress(facts.Signals))
        {
            return await RefuseAsync(command, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateRunning), cancellationToken);
        }

        if (!facts.CanStart)
        {
            return await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.PanelUpdate,
            "panel",
            accountId: null,
            TaskAudience.AdministratorOnly,
            string.IsNullOrEmpty(command.CorrelationId) ? null : command.CorrelationId,
            cancellationToken);
        if (taskId == Guid.Empty)
        {
            return await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
        }

        await _tasks.ReportAsync(taskId, RequestedPercent, RequestedLine, cancellationToken);

        var recorded = _ledger.TryWrite(taskId);
        if (recorded == PanelUpdateLedgerOutcome.AlreadyOpen)
        {
            // Another request's task is open and its ledger is not ours to touch: only this request's own
            // task is taken back.
            await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateRunning), cancellationToken);
            return await RefuseAsync(command, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateRunning), cancellationToken);
        }

        if (recorded == PanelUpdateLedgerOutcome.Failed)
        {
            await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
            return await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
        }

        await _journal.RecordRequestAsync(command.IpAddress, command.UserAgent, succeeded: true, cancellationToken);

        var created = _marker.TryCreate();
        if (created == PanelUpdateMarkerOutcome.Created)
        {
            return Result<PanelUpdateRequestedDto>.Ok(new PanelUpdateRequestedDto(taskId));
        }

        return created == PanelUpdateMarkerOutcome.AlreadyPending
            ? await AbandonAsync(command, taskId, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateRunning), cancellationToken)
            : await AbandonAsync(command, taskId, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
    }

    /// <summary>Journals a failed request and answers with the refusal.</summary>
    /// <param name="command">The request, for the journal's origin columns.</param>
    /// <param name="type">The kind of the refusal.</param>
    /// <param name="code">The refusal's code, a key of <see cref="ErrorMessages"/>.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<PanelUpdateRequestedDto>> RefuseAsync(
        StartPanelUpdateCommand command,
        ErrorType type,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordRequestAsync(command.IpAddress, command.UserAgent, succeeded: false, cancellationToken);
        return Result<PanelUpdateRequestedDto>.Fail(Error.Of(code, type));
    }

    /// <summary>Takes back what a request that could not reach root had already created, and refuses it.</summary>
    /// <param name="command">The request, for the journal's origin columns.</param>
    /// <param name="taskId">The task the request opened.</param>
    /// <param name="type">The kind of the refusal.</param>
    /// <param name="code">The refusal's code, a key of <see cref="ErrorMessages"/>.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<PanelUpdateRequestedDto>> AbandonAsync(
        StartPanelUpdateCommand command,
        Guid taskId,
        ErrorType type,
        string code,
        CancellationToken cancellationToken)
    {
        await _tasks.FailAsync(taskId, code, cancellationToken);
        _ledger.Remove();
        return await RefuseAsync(command, type, code, cancellationToken);
    }
}
