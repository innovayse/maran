//! Tests for [`StreamLogSink`].
//!
//! What is pinned here is the distinction the operator depends on: a client
//! that closed its stream and a client the agent dropped for not reading are
//! DIFFERENT endings, and the sink is the only thing that can tell them apart.
//! Collapsing them was the state this round found — three endings, one silent
//! stream close, nothing said to anyone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::{Duration, Instant};

use maran_ops::sites::{LogSink, TailEnd};
use tokio::sync::mpsc;

use super::StreamLogSink;

/// The deadline the stall tests run against — short enough that the give-up
/// path is actually exercised in a test suite rather than argued about.
const PATIENCE: Duration = Duration::from_millis(150);

#[tokio::test]
async fn a_line_delivered_to_a_reader_reports_no_ending() {
    let (sender, mut receiver) = mpsc::channel(4);
    let mut sink = StreamLogSink::new(sender);

    assert_eq!(sink.line("hello", true), Ok(()));
    assert!(sink.is_listening());

    let delivered = receiver.recv().await.unwrap().unwrap();
    match delivered.result {
        Some(crate::proto::tail_site_log_response::Result::Ok(line)) => {
            assert_eq!(line.line, "hello");
            assert!(line.historical);
        }
        other => panic!("the line must arrive as an ok payload, got {other:?}"),
    }
}

#[tokio::test]
async fn a_closed_stream_is_the_clients_own_ending() {
    let (sender, receiver) = mpsc::channel(4);
    drop(receiver);
    let mut sink = StreamLogSink::new(sender);

    // Nobody to tell, so nothing is reported: this is the one ending that must
    // NOT produce a terminal error.
    assert_eq!(sink.line("hello", false), Err(TailEnd::ClientClosed));
    assert!(!sink.is_listening());
    assert!(!TailEnd::ClientClosed.is_involuntary());
}

#[tokio::test]
async fn a_client_that_stops_reading_is_dropped_and_the_ending_says_so() {
    // Capacity one, filled and never drained: the receiver is alive, so the
    // channel is open and `is_listening` stays true — this is precisely the
    // wedged client (an exhausted HTTP/2 window, a suspended tab) that a plain
    // blocking send would have parked a blocking-pool thread on forever.
    let (sender, _receiver) = mpsc::channel(1);
    let mut sink = StreamLogSink::with_patience(sender, PATIENCE);

    assert_eq!(sink.line("first", false), Ok(()));

    let started = Instant::now();
    let outcome = sink.line("second", false);

    assert_eq!(
        outcome,
        Err(TailEnd::ClientStalled),
        "a client that stopped reading must be reported as dropped, not as closed"
    );
    assert!(
        started.elapsed() >= PATIENCE,
        "the sink must actually wait out its deadline before giving up"
    );
    assert!(
        TailEnd::ClientStalled.is_involuntary(),
        "the operator must be told about an ending the agent chose"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_slow_reader_is_waited_for_rather_than_dropped() {
    let (sender, mut receiver) = mpsc::channel(1);
    let mut sink = StreamLogSink::with_patience(sender, PATIENCE);
    assert_eq!(sink.line("first", false), Ok(()));

    // Drained after a pause that is real but inside the deadline: slow is not
    // absent, and nothing may be lost while the sink retries.
    let drain = tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(50)).await;
        let first = receiver.recv().await.unwrap();
        let second = receiver.recv().await.unwrap();
        (first, second)
    });

    // On its own thread because `line` is a blocking API by contract — it is
    // called from `spawn_blocking` in production, and calling it on the test's
    // runtime thread would stop the drain below from ever running.
    let outcome =
        std::thread::scope(|scope| scope.spawn(|| sink.line("second", false)).join().unwrap());
    assert_eq!(outcome, Ok(()), "a slow reader must be waited for");

    let (first, second) = drain.await.unwrap();
    for (response, expected) in [(first, "first"), (second, "second")] {
        match response.unwrap().result {
            Some(crate::proto::tail_site_log_response::Result::Ok(line)) => {
                assert_eq!(line.line, expected, "the retried line must not be lost");
            }
            other => panic!("expected a line, got {other:?}"),
        }
    }
}
