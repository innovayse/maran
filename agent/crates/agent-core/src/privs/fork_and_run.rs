//! The fork, and the body of the child it creates: the code every privileged entry point shares.

use std::panic::AssertUnwindSafe;

use super::child_exit_status::{EXIT_OK, EXIT_VERIFICATION_FAILED, EXIT_WORK_FAILED};
use super::close_inherited_descriptors::close_inherited_descriptors;
use super::last_errno::last_errno;
use super::priv_error::PrivError;

/// Forks; in the child runs `drop_step`, the descriptor sweep and `work`, then exits; in the
/// parent returns the child's pid for the caller to wait on.
///
/// The three entry points of this module ([`super::fork_as_account::fork_as_account`],
/// [`super::fork_as_account_for_bytes::fork_as_account_for_bytes`] and
/// [`super::fork_as_account_for_descriptor::fork_as_account_for_descriptor`]) differ in what
/// crosses back from the child and in nothing else. This function is everything they have in
/// common, so the sequence a reviewer has to trust — fork, drop, verify, sweep, work, `_exit` —
/// exists once and is not a copy that can drift in one of three places.
///
/// `drop_step` is [`super::drop_to_account::drop_to`] for every production caller. It is a
/// parameter and not a hard-wired call for one reason: a unit test is not root and cannot drop to
/// another account, yet the paths around the drop — the sweep, the channels, the exit statuses —
/// are exactly what this module's new entry points add, and they must be exercisable without
/// being root. The function is crate-private, and each public entry point passes the real drop.
///
/// `spare` is the one descriptor the child keeps (see [`close_inherited_descriptors`]).
///
/// What the child does, in order, and why — the full argument is on
/// [`super::fork_as_account::fork_as_account`], which this paragraph abbreviates:
///
/// 1. `drop_step` drops the process to the account and verifies it did. A failure is the exit
///    status naming what failed, and no work is done.
/// 2. Only then does it close what it inherited. The drop comes first because the sweep is meant
///    to protect a process that is already the customer.
/// 3. `work` runs inside `catch_unwind`: a panic must not unwind past this point, because
///    unwinding out of a fork would let a second copy of the caller's stack keep running in a
///    process the caller does not know exists.
/// 4. Every path ends in `_exit`, which — unlike `exit` — runs no atexit handlers and flushes no
///    stdio buffers. Flushing here would write the parent's buffered bytes a second time, from a
///    process the parent does not know about. `scripts/lib/check-structure.sh` rejects `exit` in
///    this module, so that invariant is a gate rather than a paragraph.
///
/// The panic HOOK is deliberately NOT replaced in the child. Silencing it would mean calling
/// `std::panic::set_hook`, which takes the standard library's process-global hook lock and drops
/// the previous hook's box — a lock and an allocator round trip on every call, in a forked child
/// of a multi-threaded process. The default hook is a hazard only on a path that must not be
/// taken at all: agent code returns errors and never panics (clippy `panic = "deny"`), and if one
/// ever does the parent's patience turns a wedged child into a killed child and a typed error.
///
/// # Errors
///
/// [`PrivError::ForkFailed`] when no child could be created, in which case no work was done and
/// the caller still owns every descriptor it holds.
pub(super) fn fork_and_run<D, W>(
    drop_step: D,
    spare: Option<libc::c_int>,
    work: W,
) -> Result<libc::pid_t, PrivError>
where
    D: FnOnce() -> Result<(), i32>,
    W: FnOnce() -> Result<(), PrivError>,
{
    // SAFETY: `fork` takes no arguments and touches no memory of ours. The child
    // it creates runs only the code below, which is confined to async-signal-safe
    // syscalls until the drop is verified, and which always ends in `_exit` — so
    // the child never returns into the caller's stack and never runs a destructor
    // for state the parent also owns.
    let child = unsafe { libc::fork() };

    // Matched, not compared. `fork` returns exactly -1 on failure, 0 in the child
    // and a pid in the parent, and a `<` comparison on that is one edit away from
    // letting -1 through to the parent arm — where `waitpid(-1, …)` waits for ANY
    // child of the root daemon and reports a stranger's exit status as this
    // operation's outcome. The three cases are named so that no fourth exists;
    // the wait refuses a non-positive pid as well, because one structural guard
    // for this is not enough (rules/security.md: defense in depth).
    match child {
        -1 => Err(PrivError::ForkFailed {
            errno: last_errno(),
        }),
        0 => {
            let status = drop_step().map_or_else(
                |failure| failure,
                |()| {
                    // A process that has just changed its uid is not dumpable under the
                    // kernel default, but `fs.suid_dumpable=1` makes it so, and a dumpable
                    // child can be ptraced and have its `/proc/<pid>/fd` opened by the
                    // customer's OTHER processes (same uid), which would let them write into
                    // the child's result channel. Set explicitly, so the isolation does not
                    // depend on a sysctl; a failure ends the child before any work.
                    //
                    // SAFETY: `prctl(PR_SET_DUMPABLE, 0)` takes integers only and touches no
                    // memory; it is async-signal-safe.
                    if unsafe { libc::prctl(libc::PR_SET_DUMPABLE, 0, 0, 0, 0) } != 0 {
                        return EXIT_VERIFICATION_FAILED;
                    }

                    // The drop verified, so this process is the customer — and it
                    // is still holding every descriptor the daemon had open at
                    // fork time. They go before `work` runs, not after.
                    close_inherited_descriptors(spare);

                    match std::panic::catch_unwind(AssertUnwindSafe(work)) {
                        Ok(Ok(())) => EXIT_OK,
                        Ok(Err(_)) | Err(_) => EXIT_WORK_FAILED,
                    }
                },
            );

            // SAFETY: `_exit` never returns and never touches memory. It is the
            // only correct way out of a forked child that shares the parent's
            // stdio.
            unsafe { libc::_exit(status) };
        }
        pid => Ok(pid),
    }
}

#[cfg(test)]
#[path = "../tests/privs/fork_and_run_tests.rs"]
mod tests;
