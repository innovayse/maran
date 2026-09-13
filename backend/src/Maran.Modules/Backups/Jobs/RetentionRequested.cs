namespace Maran.Modules.Backups.Jobs;

/// <summary>
/// Asks for one retention pass over an account's backups, published only after a scheduled run has
/// completed successfully (R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries the count rather than re-reading it</b> because the count that governs a pass is
/// the one the schedule held when the run it follows was started. A pass that re-read the schedule
/// could act on a number an operator changed while the backup was running, which would make the
/// same nightly operation delete a different set of archives depending on when a form was saved.
/// </para>
/// <para>
/// <b>Published after a SUCCESS and never after a failure.</b> Pruning first, or pruning regardless,
/// means one failed run costs the oldest good copy — and a run fails precisely on the nights when
/// the old copies matter. The publishing side is where that rule lives; this message has no way to
/// say "prune anyway", which is deliberate.
/// </para>
/// <para>
/// It carries no secret and travels the panel's local, in-memory queue.
/// </para>
/// </remarks>
/// <param name="AccountId">The account whose backups are to be pruned.</param>
/// <param name="RetainCount">How many of its most recent successful backups to keep.</param>
public sealed record RetentionRequested(Guid AccountId, int RetainCount);
