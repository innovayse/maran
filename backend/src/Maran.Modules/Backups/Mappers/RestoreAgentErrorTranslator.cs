using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// Names the failure that a restore stream's terminal event represents, for every ending except the
/// one that carries an outcome.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates; it never decides (rules/csharp.md). It is deliberately a SECOND type beside
/// <see cref="BackupAgentErrorTranslator"/> rather than a shared one taking both event kinds,
/// because the two enums are different closed sets and a single function over both would have to
/// take the union — which is a signature under which every future arm of either stream falls
/// silently into the other's default.
/// </para>
/// <para>
/// <b>What it must never do is invent a success.</b> Every kind other than
/// <see cref="BackupRestoreEventKind.Restored"/> maps to a failure code, the quiet endings included.
/// And <see cref="BackupRestoreEventKind.Restored"/> is not handled here at all: that arm carries an
/// OUTCOME, and whether the outcome is a whole restore is a comparison, not a translation — it is
/// made once in <c>RestoreRunner</c> and this type is never asked.
/// </para>
/// <para>
/// <b>Every ending this type names itself is <see cref="ErrorType.Failure"/>, and that is a
/// decision, not a default.</b> A dropped, idle, truncated or cancelled stream is the SERVER
/// failing to finish something the caller asked for correctly; telling the caller their request was
/// malformed would be both wrong and a dead end. The one ending whose kind this type does NOT
/// decide is <see cref="BackupRestoreEventKind.Failed"/>, where the agent has already stated it and
/// re-deciding it here is exactly the flattening this method exists to stop.
/// </para>
/// <para>
/// <b>A refusal of the MATERIAL is not a refusal of the REQUEST, and this is the one place that can
/// tell them apart.</b> The agent client answers every <c>ValidationFailed</c> and
/// <c>InvalidInput</c> with a shared code whose message asks the operator to correct what they
/// entered — which is right for a domain name or a cron expression somebody typed, and wrong here.
/// A restore sends the agent nothing an operator typed: the account's system user name, the
/// backup's id, the digest the panel recorded, the list of databases the panel knows the account
/// owns and the destination the backup was written to all come from the panel's own rows, and the
/// one value the customer does type — the confirmation — is matched by the handler and never sent.
/// So an agent refusal of the <see cref="ErrorType.Validation"/> KIND on a restore stream is the
/// agent refusing the ARTIFACT, and it is given this module's <c>RestoreArtifactRejected</c>,
/// which states the refusal and does not prescribe a remedy the panel cannot know applies.
/// Measured in a browser against a deliberately corrupted archive, the restore dialog showed
/// "correct them and try again" immediately above "retyping the confirmation will not change this
/// answer" — two adjacent sentences contradicting each other, in the middle of a recovery. The KIND
/// is carried unchanged, so the endpoint still answers HTTP 400; only the sentence changes, and it
/// changes here rather than in the shared table, so that every other caller of that table keeps the
/// message that is right for it.
/// </para>
/// <para>
/// <b>A refusal because the server is BUSY is not a refusal of the request either, and on this
/// stream it is knowable.</b> The agent's per-account lock refuses a second operation with
/// <c>BackupError::AlreadyRunning</c>, which the agent maps onto the wire's <c>ALREADY_EXISTS</c>
/// (<c>agent/crates/agent/src/services/backup/backup_status.rs</c>) — the same code it uses for the
/// idempotent outcome of a repeated CREATION. The panel's shared table then renders that as "this
/// already exists on your server, so nothing was created again", which on a restore is false twice
/// over: nothing was found to exist, and nothing was being created. What makes the two
/// distinguishable HERE is that only one place in the agent produces
/// <c>BackupError::AlreadyExists</c> — <c>ops/src/backup/create_backup.rs</c>, before it takes a
/// dump — so on a RESTORE stream that wire code can only be the lock. It is renamed to
/// <c>AccountBackupOperationRunning</c>, which says what happened and what to do about it, and the
/// KIND is kept: it is still a conflict and still HTTP 409.
///
/// <b>The truncated ending is the worst answer this contract can give and it is coded as its own.</b>
/// A restore that stopped talking mid-stream has left the account in a state neither side can state:
/// possibly a replaced home over half-replaced databases. Folding it into a generic failure would
/// tell an operator to retry, and a blind retry over a half-restored account is how the second
/// attempt destroys what the first left usable.
/// </para>
/// </remarks>
public static class RestoreAgentErrorTranslator
{
    /// <summary>
    /// The agent client's code for the wire's <c>ALREADY_EXISTS</c>, which on a restore stream can
    /// only be the per-account lock refusing a second operation.
    /// </summary>
    /// <remarks>
    /// A literal rather than a <c>nameof</c>: the code is declared in the agent client's own resx
    /// and this module may not reference that project's resources, the same shape
    /// <c>CronAgentErrorTranslator</c> uses for the same reason.
    /// </remarks>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

    /// <summary>Names the failure a non-successful restore event represents, and its kind.</summary>
    /// <param name="terminal">The event that ended the stream.</param>
    /// <returns>
    /// The machine-stable code to record and answer with, together with its KIND. For
    /// <see cref="BackupRestoreEventKind.Failed"/> that is the agent's own translated error — already
    /// a resx key in the agent client's tables, and passed through unchanged so that
    /// <c>ChecksumMismatch</c> and <c>RolledBack</c> stay distinguishable to whoever reads the task,
    /// and so that an artifact the agent refused as unusable answers 400 rather than 500 — except
    /// for two codes that are renamed while keeping their kind, because on a restore stream each of
    /// them can mean only one thing and the shared table's sentence describes the other: a
    /// <see cref="ErrorType.Validation"/> kind, which here can only be the artifact, and
    /// <c>AgentAlreadyExists</c>, which here can only be the per-account lock. For the endings that
    /// carry no error, one of this module's own.
    /// </returns>
    public static Error ToFailure(BackupRestoreEvent terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        if (terminal.Kind == BackupRestoreEventKind.Failed && terminal.Failure?.Type == ErrorType.Validation)
        {
            return Error.Of(nameof(ErrorMessages.RestoreArtifactRejected), ErrorType.Validation);
        }

        if (terminal.Kind == BackupRestoreEventKind.Failed
            && string.Equals(terminal.Failure?.Code, AgentAlreadyExistsCode, StringComparison.Ordinal))
        {
            return Error.Of(nameof(ErrorMessages.AccountBackupOperationRunning), ErrorType.Conflict);
        }

        return terminal.Kind switch
        {
            BackupRestoreEventKind.Dropped =>
                Error.Of(nameof(ErrorMessages.RestoreStreamDropped), ErrorType.Failure),
            BackupRestoreEventKind.Idle =>
                Error.Of(nameof(ErrorMessages.RestoreStreamIdle), ErrorType.Failure),
            BackupRestoreEventKind.Truncated =>
                Error.Of(nameof(ErrorMessages.RestoreTruncated), ErrorType.Failure),
            BackupRestoreEventKind.Cancelled =>
                Error.Of(nameof(ErrorMessages.RestoreCancelled), ErrorType.Failure),

            // Failed carries the agent's own error; Restored never reaches here, and Progress is not
            // terminal. Both of those, and any kind a later client adds, fall to the truncated code
            // — the honest reading of "this stream ended and the panel cannot say what it did".
            _ => terminal.Failure ?? Error.Of(nameof(ErrorMessages.RestoreTruncated), ErrorType.Failure),
        };
    }
}
