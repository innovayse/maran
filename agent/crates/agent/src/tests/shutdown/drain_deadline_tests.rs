//! The drain deadline starts at the stop signal and not before it.
//!
//! Under `src/tests/`, mirroring the source tree, and declared by
//! `drain_deadline.rs` with `#[path]` so it stays a child module
//! (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tokio::sync::oneshot::channel;

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
