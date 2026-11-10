using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Resources;
using Maran.Modules.Updates.Services;

namespace Maran.Modules.Updates.Commands.RequestPanelUpdateCheck;

/// <summary>
/// Handles <see cref="RequestPanelUpdateCheckCommand"/>: journals the request and then hands the root
/// check service its marker (issue #516).
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the control.</b> The audit entry is written BEFORE the marker, so a check that
/// started is never a check that was not journalled; if the marker then cannot be created the journal
/// gets a second, failed entry. A refusal that happens before any of that is journalled as a failed
/// request too.
/// </para>
/// <para>
/// <b>Nothing the caller sent is read.</b> The command holds only server-established members, and the
/// marker's content is a constant. The handler never reports a path: an IO failure becomes
/// <c>PanelUpdateUnavailable</c> and nothing about it.
/// </para>
/// <para>
/// <b>No task.</b> A check changes nothing on the server, so there is nothing to follow in the task
/// list: the screen shows the check as running while the marker or root's running record exists, and
/// shows the result through the last-check facts root records.
/// </para>
/// <para>
/// <b>A check and an update never run together.</b> The in-progress checks are advice that spares a
/// marker; the exclusive creation of the marker is the control for two checks, and the root side
/// independently takes the same lock as an update, so a check that meets a running update ends without
/// recording anything.
/// </para>
/// <para>
/// <b>A marker nobody picked up.</b> A check marker older than the grace period means the root unit
/// never started it. It is withdrawn here so the next press can create a fresh one, which is the whole
/// of the recovery: no task or ledger exists to reconcile.
/// </para>
/// </remarks>
public sealed class RequestPanelUpdateCheckCommandHandler
{
    /// <summary>Reads the panel's release state and whether an update or a check is in flight.</summary>
    private readonly PanelUpdateStateReader _reader;

    /// <summary>Hands the request to the root check service.</summary>
    private readonly PanelUpdateCheckMarker _marker;

    /// <summary>The module's audit journal.</summary>
    private readonly UpdatesAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="reader">Reads the panel's release state and whether an update or a check is in flight.</param>
    /// <param name="marker">Hands the request to the root check service.</param>
    /// <param name="journal">The module's audit journal.</param>
    public RequestPanelUpdateCheckCommandHandler(
        PanelUpdateStateReader reader,
        PanelUpdateCheckMarker marker,
        UpdatesAuditJournal journal)
    {
        _reader = reader;
        _marker = marker;
        _journal = journal;
    }

    /// <summary>Requests the check.</summary>
    /// <param name="command">The request; it holds only what the server established.</param>
    /// <param name="cancellationToken">Cancels the request before the marker is created.</param>
    /// <returns>Success once root has the request, or the reason it was refused.</returns>
    public async Task<Result> HandleAsync(RequestPanelUpdateCheckCommand command, CancellationToken cancellationToken)
    {
        var facts = _reader.Read();
        if (PanelUpdateLedgerPolicy.IsInProgress(facts.Signals))
        {
            return await RefuseAsync(command, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateRunning), cancellationToken);
        }

        if (PanelUpdateLedgerPolicy.IsCheckInProgress(facts.CheckMarkerAge))
        {
            return await RefuseAsync(command, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateCheckRunning), cancellationToken);
        }

        if (!facts.CanStart)
        {
            return await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
        }

        if (facts.CheckMarkerAge is not null && !_marker.TryRemove())
        {
            // An old marker the root unit never took: it has to go before a new one can be created.
            return await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
        }

        await _journal.RecordCheckRequestAsync(command.IpAddress, command.UserAgent, succeeded: true, cancellationToken);

        var created = _marker.TryCreate();
        if (created == PanelUpdateMarkerOutcome.Created)
        {
            return Result.Ok();
        }

        return created == PanelUpdateMarkerOutcome.AlreadyPending
            ? await RefuseAsync(command, ErrorType.Conflict, nameof(ErrorMessages.PanelUpdateCheckRunning), cancellationToken)
            : await RefuseAsync(command, ErrorType.Unavailable, nameof(ErrorMessages.PanelUpdateUnavailable), cancellationToken);
    }

    /// <summary>Journals a failed request and answers with the refusal.</summary>
    /// <param name="command">The request, for the journal's origin columns.</param>
    /// <param name="type">The kind of the refusal.</param>
    /// <param name="code">The refusal's code, a key of <see cref="ErrorMessages"/>.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result> RefuseAsync(
        RequestPanelUpdateCheckCommand command,
        ErrorType type,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordCheckRequestAsync(command.IpAddress, command.UserAgent, succeeded: false, cancellationToken);
        return Result.Fail(Error.Of(code, type));
    }
}
