using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One event from a restore stream: progress, or the way the stream ended.</summary>
/// <remarks>
/// <b>The failure is one <see cref="Error"/>, not a loose code.</b> A restore refused by the agent
/// is answered to the customer by the module that consumed this stream, and the HTTP status of that
/// answer is derived from <see cref="Error.Type"/> and from nothing else (rules/csharp.md "Every
/// failure states its KIND"). Carrying only the code here is what made a corrupt artifact — an
/// agent <c>ValidationFailed</c>, whose kind is <see cref="ErrorType.Validation"/> — answer HTTP 500:
/// the code survived the journey and the kind was re-invented at the far end. The two travel
/// together so that they cannot be separated again.
/// </remarks>
/// <param name="Kind">Whether this is progress or one of the six terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; zero otherwise.</param>
/// <param name="Stage">
/// Machine-stable stage id for progress events — <c>downloading</c>, <c>restoring_files</c>,
/// <c>restoring_databases</c>; empty otherwise.
/// </param>
/// <param name="Outcome">
/// What the restore did, on <see cref="BackupRestoreEventKind.Restored"/> and only there; null on
/// every other kind. Null is not "nothing was restored" — it is "the agent stated no outcome", and a
/// caller must not render the two the same way.
/// </param>
/// <param name="Failure">
/// The machine-stable error code AND its kind for <see cref="BackupRestoreEventKind.Failed"/>; null
/// otherwise. The agent's own sentence — which names the databases it rolled back and the ones it
/// could not — is logged at the client boundary and never carried here.
/// </param>
public sealed record BackupRestoreEvent(
    BackupRestoreEventKind Kind,
    uint Percent,
    string Stage,
    AgentRestoreOutcome? Outcome,
    Error? Failure);
