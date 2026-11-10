namespace Maran.Modules.Backups.Jobs;

/// <summary>
/// Asks for one pass over an account's pre-restore copies, published after a restore has taken one
/// (issue #479).
/// </summary>
/// <remarks>
/// It carries no count: how many copies are kept is a fixed bound of the module
/// (<see cref="Domain.Policies.AccountBackupAllowance.MaximumPreRestoreCopies"/>), not a setting an
/// operator or a schedule can move. It is separate from <see cref="RetentionRequested"/> because that
/// one carries the schedule's retain figure for the ordinary backups and is published only after a
/// scheduled run; a customer who never schedules anything still restores. It carries no secret and
/// travels the panel's local, in-memory queue.
/// </remarks>
/// <param name="AccountId">The account whose pre-restore copies are to be bounded.</param>
public sealed record PreRestoreRetentionRequested(Guid AccountId);
