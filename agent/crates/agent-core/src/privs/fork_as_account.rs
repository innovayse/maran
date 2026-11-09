//! The one and only way the agent does work as a hosting customer.

use super::account_ids::AccountIds;
use super::drop_to_account::drop_to;
use super::fork_and_run::fork_and_run;
use super::priv_error::PrivError;
use super::wait_for_child::wait_for;

/// Runs `work` in a forked child that has dropped to `ids`, and waits for it.
///
/// This is the only place in the workspace that drops privileges, and every
/// customer file operation goes through it (rules/rust.md "Privileges"). The
/// sequence, and why each step is where it is:
///
/// 1. **`fork` first.** `setuid` and its relatives apply to the whole *process*,
///    not to the calling thread. Called on a tokio worker they would drop the
///    privilege of the entire daemon — every request in flight and every request
///    afterwards — with nothing to signal it had happened. Forking first confines
///    the drop to a process that exists only to do this one job.
/// 2. **`setgroups`, then `setgid`, then `setuid`, and never any other order.**
///    Clearing the supplementary groups requires `CAP_SETGID`, which is exactly
///    what `setuid` gives up. Dropping the uid first therefore leaves a process
///    running as the customer while still holding root's supplementary groups —
///    a state that reads as "dropped" everywhere except where it matters.
/// 3. **The child verifies, then acts.** It re-reads its real and effective uid
///    and gid and its supplementary group list, and confirms that root can no
///    longer be regained, because a partially applied drop is indistinguishable
///    from a successful one when viewed from the parent.
/// 4. **Then it closes what it inherited.** `fork` copies the daemon's entire
///    descriptor table, and this module never `exec`s, so `O_CLOEXEC` does not
///    apply: without the sweep `close_inherited_descriptors` performs, a process at
///    the customer's uid would hold the agent's listening socket and every other
///    tenant's connection.
///
/// The child does the narrowest possible unit of work and `_exit`s. It must not
/// be handed a closure that takes a lock or touches the tokio runtime: only the
/// forking thread survives into the child, so a mutex another thread held at
/// fork time stays locked forever in the child. Create one directory, write one
/// file, exit.
///
/// **On allocation, precisely, because this contract used to say "must not
/// allocate freely" and every closure in the workspace broke it.** The hazard is
/// real in general — a mutex is a mutex, and the allocator's arena is one — but
/// it does not apply to the system allocator on the platforms Maran supports,
/// and pretending otherwise made the rule one that was cited and not obeyed:
/// `std::fs::create_dir_all` allocates, `OpenOptions::open` allocates a
/// `CString` for its path inside `std`, and every `*at` wrapper in this module
/// allocates one per name. glibc registers `pthread_atfork` handlers that take
/// the malloc locks in the parent before the fork and reinitialise the arenas in
/// the child (`__malloc_fork_lock_parent` / `__malloc_fork_unlock_child`), and
/// musl's `fork` takes the equivalent lock; so a child that allocates through
/// the system allocator after `fork` cannot deadlock on it. What the rule
/// therefore is:
///
/// - **Allowed**: short-lived allocation through the global allocator, of a size
///   the child chooses — a path, a name, a small buffer.
/// - **Still forbidden**: any lock this program owns; anything that touches the
///   tokio runtime; and unbounded allocation, which is a memory bound rather
///   than a deadlock question and belongs to the caller's own input validation.
/// - **Would break the argument**: replacing the global allocator with one that
///   registers no fork handlers. There is none in this workspace, and adding one
///   would need this paragraph revisited — which is why it names the mechanism
///   rather than only the conclusion.
///
/// The drop-and-verify path itself stays allocation-free regardless (a fixed
/// stack array for `getgroups`, one syscall for the descriptor sweep), because
/// that code runs BEFORE the child has confirmed it is unprivileged and is held
/// to the stricter async-signal-safety standard rather than to this one. Work
/// that can be done in the parent still should be — `ProcessFilesHost` builds
/// its temporary file name before forking — because the smallest child is still
/// the best child.
///
/// **Callers MUST invoke this from `tokio::task::spawn_blocking`, never from a
/// runtime worker.** It forks and then waits for as long as `work` takes, so on a
/// worker thread it stalls every other in-flight command (rules/rust.md "Async
/// and blocking").
///
/// The wait is bounded at `CHILD_PATIENCE` (two minutes), after which the child is SIGKILLed
/// and reaped and the caller is told. A `work` closure that hangs therefore costs
/// one blocking-pool thread for two minutes rather than forever, and leaves no
/// process running at the customer's uid holding the parent's descriptors. An
/// unbounded wait here would turn every hazard in this module into a hang, which
/// is the one outcome an operator cannot act on.
///
/// The child's outcome crosses back as an exit status and nothing else. There is
/// deliberately no pipe carrying a rich error: a channel from an unprivileged
/// child into the root parent is a surface, and this module exists to have as
/// few of those as possible. Work that must bring DATA back is not this function's
/// job: it has two siblings that each add exactly one such channel, bounded and
/// created by the parent — [`super::fork_as_account_for_bytes::fork_as_account_for_bytes`]
/// and [`super::fork_as_account_for_descriptor::fork_as_account_for_descriptor`].
/// Everything above (fork, order of the drop, verification, the sweep) is the same
/// code for all three, in `fork_and_run`.
///
/// # Errors
///
/// Returns [`PrivError::ForkFailed`] when no child could be created (no work was
/// done); [`PrivError::DropFailed`] when a drop syscall failed in the child;
/// [`PrivError::VerificationFailed`] when the drop did not fully apply and the
/// child refused to continue; [`PrivError::WorkFailed`] when `work` itself
/// returned an error or panicked; [`PrivError::ChildSignalled`] when the child
/// was killed, in which case the work may have been applied in part;
/// [`PrivError::ChildTimedOut`] when the child outlasted `CHILD_PATIENCE` (two minutes) and
/// was killed; [`PrivError::ChildNotCollected`] when it was killed and was still
/// not collectable within `REAP_PATIENCE`; [`PrivError::WaitFailed`] when the
/// child's outcome could not be collected; [`PrivError::ChildDidNotExit`] when the wait status says the child
/// neither exited nor died; and [`PrivError::UnexpectedExit`] for an exit code
/// this module does not produce.
pub fn fork_as_account<F>(ids: &AccountIds, work: F) -> Result<(), PrivError>
where
    F: FnOnce() -> Result<(), PrivError>,
{
    let child = fork_and_run(|| drop_to(ids), None, work)?;
    wait_for(child)
}
