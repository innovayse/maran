namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one pass of <c>StartupBackupReconciler</c> found: how many rows it examined and, of those,
/// how many it closed on each of the three things it could observe about them.
/// </summary>
/// <remarks>
/// <para>
/// A carrier between the pass and the things that read it — the log line and the tests — never
/// serialised, so <c>Models/</c> rather than <c>Common/</c> (rules/csharp.md, the fifth question).
/// </para>
/// <para>
/// <b>Four numbers rather than one, because a single "closed" count could not be audited.</b> The
/// three outcomes are different events for an operator: bytes that exist and must be released by
/// hand, a run that produced nothing, and a row the pass could not ask the agent about at all. An
/// operator who is told only "closed 3" cannot tell which of their customers has an unreferenced
/// archive on the disk.
/// </para>
/// <para>
/// <b><see cref="Examined"/> is not the sum of the other three</b>, and that is the point of keeping
/// it: a row the pass looked at and deliberately LEFT alone is the difference. The pass closes
/// nothing it could not observe when the agent answered, so <c>Examined</c> above the sum is the
/// honest record of rows still waiting for the next start.
/// </para>
/// </remarks>
/// <param name="Examined">Running rows the previous process left behind, i.e. candidates.</param>
/// <param name="ArtifactPresent">
/// Rows closed over an archive the agent listed as readable: the bytes exist, the panel never
/// recorded their digest, so the row is failed and the artifact is reported rather than removed.
/// </param>
/// <param name="ArtifactAbsent">
/// Rows closed over a destination that holds no readable archive for them: the run produced nothing
/// the panel can point at, and the row said it was still going.
/// </param>
/// <param name="Unobservable">
/// Rows the pass could not ask about — the destination would not resolve, or the agent refused the
/// listing. These are LEFT Running on purpose and counted so the silence is visible.
/// </param>
public sealed record BackupReclamation(
    int Examined,
    int ArtifactPresent,
    int ArtifactAbsent,
    int Unobservable);
