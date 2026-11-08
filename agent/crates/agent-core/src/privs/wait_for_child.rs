//! The parent's bounded wait for a forked child: collect, or kill and reap.

use std::time::{Duration, Instant};

use super::child_exit_status::{
    EXIT_DROP_SETGID_FAILED, EXIT_DROP_SETGROUPS_FAILED, EXIT_DROP_SETUID_FAILED, EXIT_OK,
    EXIT_VERIFICATION_FAILED, EXIT_WORK_FAILED,
};
use super::last_errno::last_errno;
use super::priv_error::PrivError;

/// How long the parent waits for a child before killing it.
///
/// The child creates a directory or writes one file, so two minutes is not a
/// budget for slow work — it is the point past which the child is not working at
/// all. Without a ceiling a wedged child costs a blocking-pool thread forever and
/// leaves a process at the customer's uid holding every descriptor the parent had
/// at fork time, which is a hang rather than an error and therefore the worst
/// signal this module could produce.
pub(super) const CHILD_PATIENCE: Duration = Duration::from_secs(120);

/// How long the parent waits for a killed child to become collectable.
///
/// A ceiling of its own, because the kill and the reap are two separate
/// mechanisms and only one of them is argued by a comment. The reap used to be a
/// blocking `waitpid` justified by "SIGKILL cannot be caught", which is true of
/// the signal this module sends and not enforced anywhere: one edit to a signal
/// number turned the recovery path into the permanent hang [`CHILD_PATIENCE`]
/// exists to prevent, and left every test green. A ceiling whose enforcement arm
/// can itself hang is not a ceiling, so the enforcement arm gets one too.
///
/// Five seconds is not a budget for the kernel to tear a process down — that
/// takes microseconds — it is the point past which the child is not dying at
/// all, which is a state the caller must be told about rather than wait out.
const REAP_PATIENCE: Duration = Duration::from_secs(5);

/// First gap between two `WNOHANG` polls of the child.
///
/// Short, because the overwhelming majority of children finish in well under a
/// millisecond and the first poll after the fork usually collects them.
const FIRST_NAP: Duration = Duration::from_millis(1);

/// Longest gap the poll interval grows to.
///
/// The gap doubles from [`FIRST_NAP`] so that a quick child is collected almost
/// immediately while a long one is not polled tens of thousands of times.
const LONGEST_NAP: Duration = Duration::from_millis(20);

/// Collects the child's outcome.
///
/// Split from [`outcome_of`], which decides what a raw wait status means, for
/// the reason every seam in this workspace exists: the decision can then be
/// tested against every status the child can produce — including the ones that
/// require a partially applied privilege drop or a kill, which no test may
/// arrange for real — while this function keeps only the `waitpid` loop that
/// needs a live child.
///
/// # Errors
///
/// As documented on [`super::fork_as_account::fork_as_account`]; `Ok(())` only for [`EXIT_OK`].
pub(super) fn wait_for(child: libc::pid_t) -> Result<(), PrivError> {
    wait_until(child, CHILD_PATIENCE)
}

/// The wait, with its ceiling supplied.
///
/// The seam exists so the give-up path can be tested rather than reasoned about,
/// which is the same argument `follow_log::follow_with_patience` was built on:
/// a test that waited out [`CHILD_PATIENCE`] would never be written, so the one
/// branch that saves a blocking-pool thread would be the one branch with no test.
///
/// # Errors
///
/// As documented on [`super::fork_as_account::fork_as_account`].
pub(super) fn wait_until(child: libc::pid_t, patience: Duration) -> Result<(), PrivError> {
    // A pid this function was given rather than one it forked itself. 0 means
    // "every process in my group" and -1 means "any child at all", and either
    // would make this call reap a process it knows nothing about and report that
    // stranger's exit status as this operation's outcome. Refused before the
    // syscall rather than trusted (rules/rust.md "Validation first").
    if child <= 0 {
        return Err(PrivError::WaitFailed {
            errno: libc::EINVAL,
        });
    }

    let deadline = Instant::now() + patience;
    let mut status: libc::c_int = 0;
    let mut nap = FIRST_NAP;

    loop {
        // SAFETY: `status` is a live, writable local `c_int`, which is what
        // `waitpid` expects; it writes nothing else. `child` is positive and is a
        // pid this function's caller just forked and has not reaped, so it cannot
        // name an unrelated process — and because neither an EINTR return nor a
        // WNOHANG "not yet" reaps anything, the pid cannot be recycled between
        // iterations of this loop either.
        let reaped = unsafe { libc::waitpid(child, &raw mut status, libc::WNOHANG) };

        if reaped == child {
            return outcome_of(status);
        }

        if reaped < 0 {
            // Restarted on EINTR, which is a path this daemon will actually take:
            // the agent registers tokio signal handlers, and `systemctl stop`
            // signals every process in the cgroup. Returning on EINTR would leave
            // the child running and never reaped — a zombie per occurrence, and a
            // caller told the outcome is unknown for work that then completes
            // anyway. Every OTHER errno must return, or a real failure becomes an
            // infinite loop, which is a worse signal than any error.
            let errno = last_errno();
            if errno != libc::EINTR {
                return Err(PrivError::WaitFailed { errno });
            }
            continue;
        }

        // `reaped == 0`: still running. The child does one narrow unit of work, so
        // reaching the deadline means it is wedged — a lock frozen at fork time, a
        // `work` closure that hangs — and waiting on it forever would cost a
        // blocking-pool thread permanently while a process at the customer's uid
        // holds every descriptor the parent had. It is killed and reaped instead.
        if Instant::now() >= deadline {
            return give_up_on(child);
        }

        std::thread::sleep(nap);
        nap = (nap * 2).min(LONGEST_NAP);
    }
}

/// Kills a child that outlasted its patience and reaps it, so nothing is left
/// running and nothing is left a zombie.
///
/// The reap has a ceiling of its own ([`REAP_PATIENCE`]) and polls rather than
/// blocking. The old shape blocked in `waitpid` forever on the reasoning that
/// SIGKILL cannot be caught — true of the signal, but nothing enforced that this
/// is the signal sent, and a catchable one turned this recovery path into the
/// permanent hang [`CHILD_PATIENCE`] was written to remove. The bound is
/// therefore in the code rather than in the argument, and a child that survives
/// what was sent to it produces a typed error instead of a stuck thread.
///
/// # Errors
///
/// [`PrivError::ChildTimedOut`] when the child was killed and collected;
/// [`PrivError::ChildNotCollected`] when it was still not collectable after
/// [`REAP_PATIENCE`]; [`PrivError::WaitFailed`] when the wait itself failed.
pub(super) fn give_up_on(child: libc::pid_t) -> Result<(), PrivError> {
    give_up_on_with(child, libc::SIGKILL, REAP_PATIENCE)
}

/// Kills a child the parent has decided to stop waiting for, and answers with the reason.
///
/// The two entry points that read data back from the child ([`super::fork_as_account_for_bytes`]
/// and [`super::fork_as_account_for_descriptor`]) can decide, before the child has finished, that
/// they will not accept what it is producing — it wrote more than the cap, or its patience ran out
/// mid-stream. Each such decision is the same three steps: kill, reap, report. `reason` is what
/// the caller was going to report; it is returned unchanged when the kill worked, and replaced
/// when the recovery itself failed, because "the child could not even be collected" is a more
/// serious fact for the operator than why it was killed.
///
/// The child is always reaped before this returns, or the failure to reap is the answer: this is
/// what keeps a refused result from leaving a zombie or a process at the customer's uid behind.
pub(super) fn abandon(child: libc::pid_t, reason: PrivError) -> PrivError {
    match give_up_on(child) {
        Err(PrivError::ChildTimedOut) | Ok(()) => reason,
        Err(recovery_failed) => recovery_failed,
    }
}

/// The give-up, with its signal and its reap ceiling supplied.
///
/// The seam exists for the same reason [`wait_until`]'s does: the outcome of a
/// child that does NOT die of what it was sent is a real branch, and without a
/// way to send a catchable signal it would be the one branch no test could
/// reach — so it would be the one branch free to become a permanent hang again.
/// Production always calls it through [`give_up_on`], with SIGKILL.
///
/// # Errors
///
/// As documented on [`give_up_on`].
fn give_up_on_with(
    child: libc::pid_t,
    signal: libc::c_int,
    patience: Duration,
) -> Result<(), PrivError> {
    // SAFETY: `kill` takes two integers and touches no memory. `child` is
    // positive — checked at the top of `wait_until` — so this cannot address a
    // process group.
    unsafe { libc::kill(child, signal) };

    let deadline = Instant::now() + patience;
    let mut status: libc::c_int = 0;
    let mut nap = FIRST_NAP;

    loop {
        // SAFETY: as in `wait_until`. `WNOHANG` rather than a blocking wait, so
        // that the deadline below can be reached at all.
        let reaped = unsafe { libc::waitpid(child, &raw mut status, libc::WNOHANG) };

        if reaped == child {
            return Err(PrivError::ChildTimedOut);
        }

        if reaped < 0 {
            let errno = last_errno();
            if errno != libc::EINTR {
                return Err(PrivError::WaitFailed { errno });
            }
            continue;
        }

        // `reaped == 0`: killed and still not collectable. Either the signal did
        // not end it or the kernel has not finished, and the caller learns which
        // outcome it has rather than waiting for one that may never come.
        if Instant::now() >= deadline {
            return Err(PrivError::ChildNotCollected);
        }

        std::thread::sleep(nap);
        nap = (nap * 2).min(LONGEST_NAP);
    }
}

/// Turns the raw status `waitpid` wrote into this module's typed outcome.
///
/// Takes the status rather than reading it, so that every ending a child can
/// have — including the two that require a privilege drop to have gone wrong —
/// is a value a test can hand it.
///
/// # Errors
///
/// As documented on [`super::fork_as_account::fork_as_account`]; `Ok(())` only for [`EXIT_OK`].
fn outcome_of(status: libc::c_int) -> Result<(), PrivError> {
    if libc::WIFSIGNALED(status) {
        // Killed part-way. The caller is told, because half a write is a state the
        // operation above this one has to converge from on its next attempt.
        return Err(PrivError::ChildSignalled {
            signal: libc::WTERMSIG(status),
        });
    }

    if !libc::WIFEXITED(status) {
        // A separate variant, not `UnexpectedExit`, because the number means
        // something different: here it is the RAW wait status, below it is an exit
        // code. One variant carrying both would print
        // "unexpected status 4991" for a child that exited 19 and for one that
        // never exited at all, and an operator could not tell which they had.
        return Err(PrivError::ChildDidNotExit { status });
    }

    match libc::WEXITSTATUS(status) {
        EXIT_OK => Ok(()),
        EXIT_DROP_SETGROUPS_FAILED => Err(PrivError::DropFailed {
            syscall: "setgroups",
        }),
        EXIT_DROP_SETGID_FAILED => Err(PrivError::DropFailed { syscall: "setgid" }),
        EXIT_DROP_SETUID_FAILED => Err(PrivError::DropFailed { syscall: "setuid" }),
        EXIT_VERIFICATION_FAILED => Err(PrivError::VerificationFailed),
        EXIT_WORK_FAILED => Err(PrivError::WorkFailed),
        other => Err(PrivError::UnexpectedExit { status: other }),
    }
}

#[cfg(test)]
#[path = "../tests/privs/wait_for_child_tests.rs"]
mod tests;
