//! What the parent concludes from the only thing the child can tell it.
//!
//! The drop itself needs root and a real hosting account, so it is exercised by
//! `crates/agent/tests/privileges_on_a_real_host.rs` inside the polygon
//! container. What is tested here is the half that runs on every machine: the
//! translation of a raw wait status into a typed outcome, for every ending a
//! child of this module can have — including the two that would require a
//! privilege drop to have gone wrong, which no test may arrange for real.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use super::{
    EXIT_DROP_FAILED, EXIT_OK, EXIT_VERIFICATION_FAILED, EXIT_WORK_FAILED, MAXIMUM_GROUPS,
    Observed, give_up_on_with, outcome_of, verify, wait_until,
};
use crate::privs::priv_error::PrivError;

/// The raw status `waitpid` writes for a child that called `_exit(code)`.
///
/// Built here rather than taken from a real child on purpose: the encoding is
/// what `WIFEXITED`/`WEXITSTATUS` read, and a test that produced it by forking
/// could only ever reach the statuses a healthy machine hands out.
fn exited_with(code: i32) -> libc::c_int {
    (code & 0xff) << 8
}

/// The raw status `waitpid` writes for a child killed by `signal`.
fn killed_by(signal: libc::c_int) -> libc::c_int {
    signal & 0x7f
}

#[test]
fn a_child_that_did_the_work_reports_success() {
    assert_eq!(outcome_of(exited_with(EXIT_OK)), Ok(()));
}

#[test]
fn a_child_whose_drop_syscall_failed_reports_drop_failed() {
    assert_eq!(
        outcome_of(exited_with(EXIT_DROP_FAILED)),
        Err(PrivError::DropFailed)
    );
}

#[test]
fn a_child_whose_drop_did_not_apply_reports_verification_failed() {
    assert_eq!(
        outcome_of(exited_with(EXIT_VERIFICATION_FAILED)),
        Err(PrivError::VerificationFailed)
    );
}

#[test]
fn a_child_whose_work_failed_reports_work_failed() {
    assert_eq!(
        outcome_of(exited_with(EXIT_WORK_FAILED)),
        Err(PrivError::WorkFailed)
    );
}

#[test]
fn a_failed_drop_is_never_reported_as_a_failed_work_closure() {
    // The two say different things to an operator: one is a customer's
    // operation that did not work, the other is the privilege machinery
    // refusing. A mapping that collapsed them would be silent.
    assert_ne!(
        outcome_of(exited_with(EXIT_DROP_FAILED)),
        outcome_of(exited_with(EXIT_WORK_FAILED))
    );
}

#[test]
fn a_status_this_module_never_produces_is_reported_as_unexpected() {
    assert_eq!(
        outcome_of(exited_with(1)),
        Err(PrivError::UnexpectedExit { status: 1 })
    );
}

#[test]
fn a_child_killed_by_a_signal_is_reported_as_signalled_not_as_an_exit_status() {
    assert_eq!(
        outcome_of(killed_by(libc::SIGKILL)),
        Err(PrivError::ChildSignalled {
            signal: libc::SIGKILL
        })
    );
}

#[test]
fn a_kill_carrying_the_success_status_in_its_low_bits_is_still_a_kill() {
    // `WEXITSTATUS` of a signalled status reads the same byte a healthy exit
    // writes, so a mapping that asked "did it exit 0?" first would report a
    // killed child as a completed one — with half a write on disk.
    assert_eq!(
        outcome_of(killed_by(libc::SIGTERM)),
        Err(PrivError::ChildSignalled {
            signal: libc::SIGTERM
        })
    );
}

#[test]
fn a_child_that_neither_exited_nor_died_is_named_by_its_own_variant() {
    // The raw status and an exit code are different numbers with the same shape,
    // so they get different variants: an operator reading one must not have to
    // guess which they are holding.
    let stopped = (libc::SIGSTOP << 8) | 0x7f;

    assert_eq!(
        outcome_of(stopped),
        Err(PrivError::ChildDidNotExit { status: stopped })
    );
    assert_ne!(
        outcome_of(stopped),
        Err(PrivError::UnexpectedExit { status: stopped })
    );
}

/// The credentials of a child whose drop applied completely.
fn a_clean_drop(uid: libc::uid_t, gid: libc::gid_t) -> Observed {
    let mut groups = [0 as libc::gid_t; MAXIMUM_GROUPS];
    groups[0] = gid;

    Observed {
        real_uid: uid,
        effective_uid: uid,
        real_gid: gid,
        effective_gid: gid,
        groups,
        group_count: 1,
        root_regained: false,
    }
}

#[test]
fn a_drop_that_applied_completely_is_accepted() {
    assert_eq!(verify(1000, 1000, &a_clean_drop(1000, 1000)), Ok(()));
}

#[test]
fn a_drop_that_moved_only_the_effective_uid_is_refused() {
    // What `seteuid` leaves behind, and what a `setuid` that returned success
    // without applying looks like: the process reads as the customer to anything
    // that asks `geteuid`, and is root to the kernel.
    let mut seen = a_clean_drop(1000, 1000);
    seen.real_uid = 0;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_drop_that_moved_only_the_effective_gid_is_refused() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.real_gid = 0;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_supplementary_group_that_survived_the_drop_is_refused() {
    // The whole reason `setgroups` runs first: an extra group here means the
    // child is running as the customer while still in one of root's groups.
    let mut seen = a_clean_drop(1000, 1000);
    seen.groups[1] = 0;
    seen.group_count = 2;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_group_list_that_did_not_fit_is_refused_rather_than_ignored() {
    // -1 from `getgroups` means the list is longer than the child will look at,
    // which after a correct `setgroups([gid])` is impossible — so it is evidence
    // the call did not take effect, not a large but acceptable list.
    let mut seen = a_clean_drop(1000, 1000);
    seen.group_count = -1;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn an_empty_group_list_is_accepted_because_setgroups_may_leave_one() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.group_count = 0;

    assert_eq!(verify(1000, 1000, &seen), Ok(()));
}

#[test]
fn a_single_group_that_is_not_the_accounts_own_is_refused() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.groups[0] = 27;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_child_that_could_ask_for_root_and_get_it_is_refused_though_every_id_reads_correctly() {
    // Every declarative check passes here: this is the saved-set uid case, which
    // `getuid` and `geteuid` cannot see and which lets anything the child later
    // executes climb straight back to root.
    let mut seen = a_clean_drop(1000, 1000);
    seen.root_regained = true;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

/// How long a bounded-wait test gives its own body before declaring it stuck.
///
/// Every test below waits on a process, and the mutations they defend against —
/// a deadline that never fires, an errno loop that never exits — turn a red test
/// into a hung one. A hang is read as a flaky runner and retried; the bound is
/// what makes the failure a failure.
const TEST_WATCHDOG: Duration = Duration::from_secs(20);

/// Runs `body` on its own thread and fails the test if it outlasts
/// [`TEST_WATCHDOG`].
fn within<T: Send + 'static>(what: &str, body: impl FnOnce() -> T + Send + 'static) -> T {
    let (sender, receiver) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(body());
    });

    match receiver.recv_timeout(TEST_WATCHDOG) {
        Ok(value) => value,
        Err(_) => panic!("{what} did not finish within {TEST_WATCHDOG:?}"),
    }
}

/// A child of this process that sleeps far longer than any test will wait.
///
/// `/bin/sleep` rather than a forked closure: what is under test is the parent's
/// half — the deadline, the kill and the reap — and that half does not care how
/// the child was made.
fn a_child_that_will_not_finish() -> std::process::Child {
    std::process::Command::new("/bin/sleep")
        .arg("600")
        .spawn()
        .expect("/bin/sleep exists on every host in the support matrix")
}

#[test]
fn a_child_that_outlasts_the_patience_is_killed_rather_than_waited_on_forever() {
    let mut child = a_child_that_will_not_finish();
    let pid = libc::pid_t::try_from(child.id()).expect("a pid fits in pid_t");

    // Not `within`, and the difference is the whole point of this test. If the
    // deadline stops firing, the wait never returns — and the abandoned thread
    // holds a live `/bin/sleep` that inherited this binary's stdout, so cargo
    // keeps reading that pipe and the RUN hangs rather than the test failing.
    // That is a hang blamed on the runner and retried, which is how a removed
    // deadline survives. So the watchdog kills the child before it panics: the
    // wait then returns at once, the thread ends, the pipe closes, and what the
    // operator sees is one named red test.
    let (sender, receiver) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(wait_until(pid, Duration::from_millis(50)));
    });
    let outcome = match receiver.recv_timeout(TEST_WATCHDOG) {
        Ok(value) => value,
        Err(_) => {
            let _ = child.kill();
            let _ = child.wait();
            panic!("wait_until did not give up on the child within {TEST_WATCHDOG:?}");
        }
    };

    assert_eq!(outcome, Err(PrivError::ChildTimedOut));
    // Reaped by `give_up_on`, so the standard library has nothing left to collect
    // — which is what proves the kill was followed by a wait rather than leaving
    // a zombie behind.
    assert!(
        child.try_wait().is_err() || child.try_wait().is_ok_and(|status| status.is_none()),
        "the child must already have been collected by the wait under test"
    );
}

/// A child that refuses SIGTERM and outlives any wait a test will make.
///
/// `exec` is what makes it the right child: without it the pid under test is a
/// shell whose grandchild does the sleeping, so a kill ends the shell, leaves
/// the grandchild alive holding this binary's descriptors, and the run hangs
/// instead of failing. Its stdio is null for the same reason — an inherited
/// stdout that never closes is a pipe cargo waits on forever.
fn a_child_that_ignores_being_asked_nicely() -> std::process::Child {
    // The child announces that its trap is installed, and the caller waits for
    // that announcement. Without it the test races the shell's startup: a signal
    // arriving before the `trap` line kills the child, the wait collects it, and
    // the test passes for a reason that has nothing to do with what it asserts.
    let ready = std::env::temp_dir().join(format!(
        "maran-privs-trap-ready-{}-{:?}",
        std::process::id(),
        std::thread::current().id()
    ));
    let _ = std::fs::remove_file(&ready);

    let child = std::process::Command::new("/bin/sh")
        .args([
            "-c",
            "trap '' TERM; : > \"$1\"; exec sleep 600",
            "sh",
            &ready.to_string_lossy(),
        ])
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .spawn()
        .expect("/bin/sh exists on every host in the support matrix");

    let deadline = std::time::Instant::now() + TEST_WATCHDOG;
    while !ready.exists() {
        assert!(
            std::time::Instant::now() < deadline,
            "the child never announced that it ignores SIGTERM"
        );
        std::thread::sleep(Duration::from_millis(1));
    }
    let _ = std::fs::remove_file(&ready);

    child
}

#[test]
fn a_child_that_ignores_sigterm_is_still_given_up_on() {
    // The give-up path is the one that must not be able to hang: it holds a
    // blocking-pool thread and a process at the customer's uid while it waits.
    // A catchable signal plus a blocking reap is exactly that hang, one word
    // away, and this is the test that sees it — the child ignores SIGTERM, so
    // only an uncatchable signal ends it, and only a bounded reap returns
    // without one.
    let mut child = a_child_that_ignores_being_asked_nicely();
    let pid = libc::pid_t::try_from(child.id()).expect("a pid fits in pid_t");

    let (sender, receiver) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(wait_until(pid, Duration::from_millis(50)));
    });
    let outcome = match receiver.recv_timeout(TEST_WATCHDOG) {
        Ok(value) => value,
        Err(_) => {
            let _ = child.kill();
            let _ = child.wait();
            panic!("give_up_on hung on a child that ignores SIGTERM");
        }
    };

    // Killed and collected — not `ChildNotCollected`, which is what a child that
    // survived the signal produces once the reap has a ceiling of its own.
    assert_eq!(outcome, Err(PrivError::ChildTimedOut));
    let _ = child.kill();
    let _ = child.try_wait();
}

#[test]
fn a_child_that_survives_the_signal_is_reported_rather_than_waited_on() {
    // The branch that exists so the reap can have a ceiling at all: the child
    // was killed and is still there. Reached through the seam with a catchable
    // signal, because nothing survives the one production sends.
    let mut child = a_child_that_ignores_being_asked_nicely();
    let pid = libc::pid_t::try_from(child.id()).expect("a pid fits in pid_t");

    let outcome = within("the give-up on an unkillable child", move || {
        give_up_on_with(pid, libc::SIGTERM, Duration::from_millis(50))
    });

    assert_eq!(outcome, Err(PrivError::ChildNotCollected));
    let _ = child.kill();
    let _ = child.wait();
}

#[test]
fn a_child_that_finishes_inside_the_patience_is_collected_normally() {
    let mut child = std::process::Command::new("/bin/sleep")
        .arg("0")
        .spawn()
        .expect("/bin/sleep exists on every host in the support matrix");
    let pid = libc::pid_t::try_from(child.id()).expect("a pid fits in pid_t");

    let outcome = within("the ordinary wait", move || {
        wait_until(pid, Duration::from_secs(10))
    });

    assert_eq!(outcome, Ok(()));
    let _ = child.try_wait();
}

#[test]
fn waiting_on_a_process_that_is_not_our_child_fails_rather_than_looping() {
    // `waitpid` answers ECHILD here. Treating anything but EINTR as "try again"
    // would turn this into an infinite loop — a hang instead of a `WaitFailed`,
    // which is the worse of the two by far.
    let outcome = within("the wait on a stranger", || {
        wait_until(1, Duration::from_secs(10))
    });

    assert_eq!(
        outcome,
        Err(PrivError::WaitFailed {
            errno: libc::ECHILD
        })
    );
}

#[test]
fn waiting_on_any_child_at_all_is_refused_before_the_syscall() {
    // -1 and 0 are the two `waitpid` arguments that mean "somebody else's child
    // will do". Reaching the syscall with either would let this module report a
    // stranger's exit status as the outcome of a customer's operation.
    for stray in [-1, 0] {
        assert_eq!(
            wait_until(stray, Duration::from_secs(10)),
            Err(PrivError::WaitFailed {
                errno: libc::EINVAL
            }),
            "a non-positive pid must never reach waitpid"
        );
    }
}
