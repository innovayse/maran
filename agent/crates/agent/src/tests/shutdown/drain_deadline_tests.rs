//! The drain deadline starts at the stop signal and not before it — and what
//! its expiry does, and does not, do to work already running.
//!
//! Under `src/tests/`, mirroring the source tree, and declared by
//! `drain_deadline.rs` with `#[path]` so it stays a child module
//! (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use tokio::sync::oneshot::channel;

use crate::services::wire::run_blocking::run_blocking;
use crate::services::wire::system_failure::system_failure;
use crate::shutdown::drain_deadline::{DRAIN_BUDGET, drain_deadline};

/// The clock is paused and advanced by the test, never waited on: a test that
/// slept out the real budget would take half a minute and would measure the
/// machine's load rather than the deadline.
#[tokio::test(start_paused = true)]
async fn the_deadline_does_not_fire_while_no_stop_has_been_asked_for() {
    let (signalled, deadline) = channel::<()>();

    let mut waiting = Box::pin(drain_deadline(deadline));

    // Polled BEFORE the clock is advanced, and the order is the whole test.
    // `advance` only fires timers that have been registered, so a future nobody
    // has polled stays pending whatever the clock does — which means the
    // assertion below would hold just as well against a `drain_deadline` that
    // did not wait for the signal at all. Measured, by making exactly that
    // mutation: the test passed. It fails now.
    assert!(
        futures_poll_is_pending(&mut waiting),
        "sanity: not ready at once"
    );
    tokio::time::advance(DRAIN_BUDGET * 100).await;

    assert!(
        futures_poll_is_pending(&mut waiting),
        "a daemon that has been up for a month and has never been signalled must \
         not have its stop deadline expire"
    );

    drop(signalled);
}

/// The whole point of the bound: once the stop is asked for, the deadline
/// really does arrive, so an in-flight request cannot hold the daemon open.
#[tokio::test(start_paused = true)]
async fn the_deadline_fires_one_budget_after_the_stop_signal() {
    let (signalled, deadline) = channel::<()>();
    let waiting = tokio::spawn(drain_deadline(deadline));

    signalled
        .send(())
        .expect("the deadline half is still alive");
    tokio::time::advance(DRAIN_BUDGET + std::time::Duration::from_secs(1)).await;

    waiting
        .await
        .expect("the deadline task must complete, not panic");
}

/// A cancelled shutdown future drops the sender without sending. Treating that
/// as "no stop happened" would leave the daemon with no bound at all, which is
/// the failure the unit exists to prevent, so it must start the clock.
#[tokio::test(start_paused = true)]
async fn a_dropped_sender_starts_the_deadline_rather_than_disabling_it() {
    let (signalled, deadline) = channel::<()>();
    let waiting = tokio::spawn(drain_deadline(deadline));

    drop(signalled);
    tokio::time::advance(DRAIN_BUDGET + std::time::Duration::from_secs(1)).await;

    waiting
        .await
        .expect("the deadline task must complete, not panic");
}

/// Answers whether `future` is still pending, without a runtime of its own.
///
/// Written out rather than pulled from a helper crate: the agent depends on no
/// futures utility crate, and one line of `Context` is cheaper than a
/// dependency on the root daemon's tree (rules/security.md item 11).
fn futures_poll_is_pending<F: std::future::Future>(future: &mut std::pin::Pin<Box<F>>) -> bool {
    let waker = std::task::Waker::noop();
    let mut context = std::task::Context::from_waker(waker);
    future.as_mut().poll(&mut context).is_pending()
}

/// Bound on every cross-thread handshake below.
///
/// Generous on purpose: each handshake is expected to complete at once, so this
/// is only the difference between a NAMED failure and a hung test binary. A
/// test that waited unbounded on a property that had broken would report
/// nothing at all, which rules/testing.md counts as no check.
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(10);

/// How long the runtime-drop case gives itself before releasing the blocking
/// work, so that a drop which did NOT wait is observed as one that returned
/// early rather than as a race the machine happened to win.
const SETTLE: Duration = Duration::from_millis(250);

/// The shipped unit file, read at COMPILE time: a missing or renamed unit file
/// must be a build failure here, not a test that quietly stops checking.
const UNIT_FILE: &str = include_str!("../../../../../../installer/systemd/maran-agent.service");

/// The stop path itself, for the three source-level properties at the end of this
/// file. Read the same way and for the same reason.
const SERVER_SOURCE: &str = include_str!("../../server.rs");

/// The setting in the unit file that really bounds a stop.
const STOP_TIMEOUT_SETTING: &str = "TimeoutStopSec=";

/// The suffix systemd allows on a whole-second time span, and which the shipped
/// unit file happens not to use.
///
/// It is optional there and not here by accident: systemd's default unit for a
/// bare number in `TimeoutStopSec=` IS seconds, so `45` and `45s` mean the same
/// thing. A check that read only the suffixed form refused the shipped file and
/// reported the property as unreadable — a red check that measured nothing,
/// which is the failure mode its own message warns about.
const WHOLE_SECONDS_SUFFIX: char = 's';

/// The one startup reconciliation `server::serve` performs today.
const THE_ONLY_RECONCILER: &str = "recover_restores()";

/// `main`'s handling of a `serve` that returned an error, read for the same
/// reason [`UNIT_FILE`] is: the property below is about what `serve` must NOT
/// return, and it is only dangerous because of this line.
const MAIN_SOURCE: &str = include_str!("../../main.rs");

/// The abrupt exit `main` performs when `serve` fails.
const ABRUPT_EXIT: &str = "std::process::exit(FAILURE_EXIT_CODE)";

/// Where the expiry arm of `server::serve`'s `select!` begins.
const EXPIRY_ARM: &str = "() = drain_deadline(deadline) =>";

/// The `?` that would turn the expiry arm into an error return.
const ERROR_PROPAGATION: char = '?';

/// Expiring the budget abandons the REQUEST and not the host work — the whole
/// hazard, asserted rather than reasoned about.
///
/// `server::serve` drops the serving future when this deadline wins its
/// `select!`. Every unit of host work is inside `spawn_blocking`, so what that
/// drop reaches is a `JoinHandle`, and dropping one DETACHES the blocking task
/// rather than cancelling it. This drives the production wrapper every unary
/// rpc goes through and drops it exactly where the budget does.
///
/// Real threads and no paused clock, because the subject is a blocking-pool
/// thread: tokio's time pause does not reach `std::thread`, and a fake clock
/// would be measuring the test's own scaffolding.
#[tokio::test]
async fn expiring_the_budget_abandons_the_request_and_not_the_host_work() {
    let (has_started, work_has_started) = std::sync::mpsc::channel::<()>();
    let (may_finish, permission_to_finish) = std::sync::mpsc::channel::<()>();
    let (has_finished, work_has_finished) = std::sync::mpsc::channel::<()>();

    let mut request = Box::pin(run_blocking(
        "probe operation",
        |failure: &String| system_failure(failure.clone()),
        move || {
            let _ = has_started.send(());
            // `recv_timeout` and not `recv`: under the mutation that proves this
            // test can fail — a wrapper that runs the operation on the async
            // side instead of the pool — this closure runs on the polling
            // thread, and an unbounded wait there would hang the whole target
            // instead of naming the defect below.
            let _ = permission_to_finish.recv_timeout(HANDSHAKE_TIMEOUT);
            let _ = has_finished.send(());
            Ok::<(), String>(())
        },
    ));

    assert!(
        futures_poll_is_pending(&mut request),
        "sanity: the request cannot answer while its own host work is still \
         running — a wrapper that ran the operation on the async side would \
         have answered here, and this test would then be measuring nothing"
    );
    work_has_started
        .recv_timeout(HANDSHAKE_TIMEOUT)
        .expect("the blocking work must reach the pool before the budget expires");

    // Exactly what the expiry arm of `server::serve` does to an in-flight
    // request: the future is dropped, and with it the only handle to the work.
    drop(request);

    may_finish.send(()).expect(
        "the abandoned work is still there to release: a receiver that \
                 had been dropped would mean the task really was cancelled",
    );
    work_has_finished.recv_timeout(HANDSHAKE_TIMEOUT).expect(
        "a detached blocking task runs to completion: the budget stops the \
         daemon answering, it does not stop the useradd, the rename or the \
         database load. If this ever fails, cancellation has become possible \
         and shutdown/mod.rs, DRAIN_BUDGET and server::serve all say otherwise",
    );
}

/// And the consequence for the exit: the process cannot leave before the work
/// it abandoned returns.
///
/// `main` returns after `serve`, which drops the runtime, and dropping a tokio
/// runtime joins every blocking thread with NO timeout. So the stop is bounded
/// by `TimeoutStopSec`, never by [`DRAIN_BUDGET`] — the property the unit file
/// and the module documentation both now depend on.
#[test]
fn the_process_cannot_exit_before_the_work_it_abandoned_returns() {
    let (has_started, work_has_started) = std::sync::mpsc::channel::<()>();
    let (may_finish, permission_to_finish) = std::sync::mpsc::channel::<()>();
    let finished = Arc::new(AtomicBool::new(false));

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .expect("a runtime can be built");

    let work_finished = Arc::clone(&finished);
    runtime.spawn_blocking(move || {
        let _ = has_started.send(());
        let _ = permission_to_finish.recv_timeout(HANDSHAKE_TIMEOUT);
        work_finished.store(true, Ordering::SeqCst);
    });

    work_has_started
        .recv_timeout(HANDSHAKE_TIMEOUT)
        .expect("the blocking work must have started before the runtime is dropped");

    // Released from another thread, and only after a settle: a drop that did
    // not wait returns in microseconds and is caught by the assertion, instead
    // of racing the release and passing on a fast machine.
    let releaser = std::thread::spawn(move || {
        std::thread::sleep(SETTLE);
        let _ = may_finish.send(());
    });

    drop(runtime);

    assert!(
        finished.load(Ordering::SeqCst),
        "dropping the runtime must join the blocking pool: if it returned first, \
         the agent's exit would abandon a half-run useradd or rename outright, \
         which is a different and worse shutdown from the one documented"
    );
    releaser
        .join()
        .expect("the releasing thread does not panic");
}

/// The budget must stay below the unit's `TimeoutStopSec=`, and until now that
/// MUST was written in a doc comment with nothing reading it.
///
/// It matters more since the property above: the kill at `TimeoutStopSec` is
/// what actually interrupts host work, so the two numbers being the wrong way
/// round would mean systemd killing the daemon before it had even stopped
/// accepting — the drain becoming decoration exactly as `DRAIN_BUDGET` warns.
#[test]
fn the_drain_budget_stays_below_the_unit_files_stop_timeout() {
    let stop_timeout = UNIT_FILE
        .lines()
        .map(str::trim)
        .find(|line| line.starts_with(STOP_TIMEOUT_SETTING))
        .map(|line| line.trim_start_matches(STOP_TIMEOUT_SETTING).trim())
        .map(|span| span.strip_suffix(WHOLE_SECONDS_SUFFIX).unwrap_or(span))
        .and_then(|seconds| seconds.parse::<u64>().ok())
        .map(Duration::from_secs)
        .expect(
            "the unit file must state TimeoutStopSec in whole seconds, bare or \
             with an `s` — if this fails, the setting was renamed, removed or \
             given another of systemd's unit suffixes (`45sec`, `1min`, `45ms`), \
             which this check deliberately refuses to guess at rather than \
             reading as a number it would then compare wrongly. Widen the parse \
             with the new form; do not delete the check",
        );

    assert!(
        DRAIN_BUDGET < stop_timeout,
        "DRAIN_BUDGET is {DRAIN_BUDGET:?} and the unit file's TimeoutStopSec is \
         {stop_timeout:?}: systemd's SIGKILL would arrive before the drain had \
         finished, so no request would ever be given the budget this constant \
         promises"
    );
}

/// The gap this lane deliberately did NOT close, pinned so the note cannot
/// outlive it.
///
/// When the budget expires, nothing anywhere records WHICH operations were
/// abandoned. The next start reconciles interrupted restores and nothing else,
/// so a half-applied config write — `ops::safe_write` renames before it
/// validates — survives a kill with nobody looking. Closing that means an
/// in-flight record written at expiry and a reconciler that reads it at start,
/// which touches services outside this lane's writable set and changes what a
/// stop promises; it is the owner's call, and the document that states the gap
/// is `docs/superpowers/notes/2026-09-08-agent-shutdown-threat-note.md`.
///
/// This test goes red the day it IS closed, in either of the two ways it can
/// be, so whoever closes it is sent to the documentation that still says it is
/// open.
#[test]
fn the_stop_path_still_records_nothing_about_the_work_it_abandons() {
    let reconcilers = SERVER_SOURCE
        .lines()
        .map(str::trim)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| line.contains(THE_ONLY_RECONCILER))
        .count();

    assert_eq!(
        reconcilers, 1,
        "server::serve performs {reconcilers} startup reconciliation(s) named \
         {THE_ONLY_RECONCILER}. One is the shipped state: restores, and nothing \
         else. A second one means the in-flight gap is being closed — update \
         DRAIN_BUDGET's doc comment, shutdown/mod.rs and the report before \
         changing this test. None means this check has gone blind"
    );

    assert!(
        SERVER_SOURCE.contains("Nothing records which"),
        "the warning logged when the budget expires no longer admits that \
         nothing records the abandoned work. Either that admission was deleted \
         while the gap remains — which leaves an operator reading a log line \
         that hides it — or the gap was closed, in which case the assertion \
         above and the shutdown documentation need the same edit"
    );
}

/// The budget's expiry must leave `serve` returning `Ok`, because the only other
/// way out of `main` kills the very work the drain exists to protect.
///
/// `main` turns a `serve` error into `std::process::exit`, and `process::exit`
/// runs NO destructors: the runtime is never dropped, so the blocking pool is
/// never joined and every detached `useradd`, `rename` or database load dies
/// mid-syscall instead of finishing. That is the abrupt death this whole module
/// replaced, reached by a different door — and the door is one character wide.
/// Measured on this branch with a probe shaped like `serve`: the graceful route
/// exited after 2.00 s having let 2000 ms of blocking work finish, and the same
/// probe with `process::exit` in place of the return exited after 0.20 s with
/// the work abandoned.
///
/// Read from the source rather than executed, and deliberately: `serve` binds a
/// real socket and the budget is thirty seconds, so the behavioural route would
/// be a thirty-second test of a one-line property. The first assertion is what
/// keeps this from going quietly blind — it fails if the coupling it is guarding
/// against ever stops existing.
#[test]
fn the_expiry_arm_returns_ok_rather_than_the_error_that_would_exit_abruptly() {
    assert!(
        MAIN_SOURCE.contains(ABRUPT_EXIT),
        "main no longer exits with {ABRUPT_EXIT} when serve returns an error. \
         That coupling is the entire reason for the assertion below; without it \
         this check is guarding nothing and must be rewritten or removed rather \
         than left passing"
    );

    let expiry_arm = SERVER_SOURCE
        .split_once(EXPIRY_ARM)
        .map(|(_, rest)| rest)
        .and_then(|rest| rest.split_once("\n    }"))
        .map(|(arm, _)| arm)
        .unwrap_or_else(|| {
            panic!(
                "server::serve must still contain the expiry arm \
                 `{EXPIRY_ARM}` and the select! that closes it. If this fails \
                 the shutdown select was restructured, and this check has gone \
                 blind rather than green"
            )
        });

    assert!(
        !expiry_arm.contains(ERROR_PROPAGATION),
        "the expiry arm of server::serve's select! propagates an error:\n{expiry_arm}\n\
         An error there reaches main, which calls {ABRUPT_EXIT}, which runs no \
         destructors — so the tokio runtime is never dropped, the blocking pool \
         is never joined, and the half-finished root work the budget just \
         abandoned is killed mid-syscall instead of being allowed to finish. \
         Expiry must log and return Ok"
    );
}
