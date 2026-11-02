namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one create run produced, as the panel's own terms: the single answer the row, the panel
/// task and the audit entry are all written from. One of exactly two cases, each its own type, so
/// the artifact's figures exist only on the case that has an artifact and the failure code only on
/// the case that failed.
/// </summary>
/// <remarks>
/// <para>
/// A carrier between this module's own layers, never serialised (rules/csharp.md "Models/"). It
/// mirrors no entity and states no rule; it exists so that the three things a finished run has to
/// write cannot be derived from three different readings of the agent's stream.
/// </para>
/// <para>
/// <b>Why success is a type and not "the failure code is empty".</b> The two are equivalent only
/// while everybody remembers they are, and the failure this module exists to avoid — a row saying
/// Completed over a run that produced nothing — is exactly what one forgetful reading of that
/// convention produces. The case is the answer: a <see cref="CreatedBackupOutcome"/> has a digest
/// and a size because it has an artifact, and a <see cref="FailedBackupOutcome"/> has neither
/// because it has none, so there is no zero or empty string to be mistaken for a measurement.
/// </para>
/// </remarks>
public abstract record BackupRunOutcome
{
    /// <summary>Closes the hierarchy to the two cases this module declares.</summary>
    private protected BackupRunOutcome()
    {
    }
}
