namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one create run produced, as the panel's own terms: the single answer the row, the panel
/// task and the audit entry are all written from.
/// </summary>
/// <remarks>
/// <para>
/// A carrier between this module's own layers, never serialised (rules/csharp.md "Models/"). It
/// mirrors no entity and states no rule; it exists so that the three things a finished run has to
/// write cannot be derived from three different readings of the agent's stream.
/// </para>
/// <para>
/// <b>Why success is a field and not "the failure code is empty".</b> The two are equivalent only
/// while everybody remembers they are, and the failure this module exists to avoid — a row saying
/// Completed over a run that produced nothing — is exactly what one forgetful reading of that
/// convention produces. One field, read by everything.
/// </para>
/// </remarks>
/// <param name="Succeeded">Whether the agent reported a finished artifact.</param>
/// <param name="SizeBytes">The artifact's size; zero unless <paramref name="Succeeded"/>.</param>
/// <param name="Sha256">The artifact's digest, hex and lowercase; empty unless <paramref name="Succeeded"/>.</param>
/// <param name="DatabaseCount">How many dumps the archive holds; zero unless <paramref name="Succeeded"/>.</param>
/// <param name="FailureCode">The machine-stable failure code; empty when <paramref name="Succeeded"/>.</param>
public sealed record BackupRunOutcome(
    bool Succeeded,
    long SizeBytes,
    string Sha256,
    int DatabaseCount,
    string FailureCode);
