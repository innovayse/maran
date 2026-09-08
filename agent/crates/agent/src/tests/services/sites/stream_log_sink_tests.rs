//! Tests for [`StreamLogSink`].
//!
//! What is pinned here is the distinction the operator depends on: a client
//! that closed its stream and a client the agent dropped for not reading are
//! DIFFERENT endings, and the sink is the only thing that can tell them apart.
//! Collapsing them was the state this round found — three endings, one silent
//! stream close, nothing said to anyone.
//!
//! Every deadline here is measured on an INJECTED clock, and every retry is a
//! synchronisation point rather than a duration. The earlier version of this
//! file shortened the real deadline to 150 ms and then asserted against wall
//! time, which is a race with a shorter fuse rather than no race: on a loaded
//! machine the reader that was supposed to drain "inside the deadline" is not
//! scheduled inside it, and the sink correctly reports a stalled client while
//! the test calls it a failure. Nothing below sleeps, waits, or spawns for the
//! deadline's sake, so the answers do not depend on the machine — with the one
//! exception that says so in its own name and is a witness about tokio, not
//! about this sink.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use maran_ops::sites::{LogSink, TailEnd};
use tokio::sync::mpsc;
use tonic::Status;

use crate::proto::{AgentError, ErrorCode, TailSiteLogResponse};

use super::super::sink_clock::SinkClock;
use super::{RETRY_INTERVAL, StreamLogSink};

/// The deadline the stall tests run against.
///
/// It costs nothing to make it long: no test below waits it out in real time.
/// Ten retries' worth, so an off-by-one retry is visible in the assertions.
const PATIENCE: Duration = Duration::from_secs(30);

/// One message as it travels down the stream's channel.
type Message = Result<TailSiteLogResponse, Status>;

/// How much virtual time a clock has been asked to wait, shared with the test.
type Waited = Arc<Mutex<Duration>>;

/// A clock that answers with virtual time and never sleeps.
///
/// It makes the deadline an observable quantity: the sink's own record of how
/// long it waited, in the sink's own units, instead of an elapsed-wall-time
/// measurement that a loaded host answers differently.
struct VirtualClock {
    /// The instant virtual time started from.
    start: Instant,
    /// How far virtual time has been advanced, readable by the test.
    waited: Waited,
}

impl VirtualClock {
    /// A clock at zero, with the handle the test reads the total through.
    fn new() -> (Self, Waited) {
        let waited: Waited = Arc::new(Mutex::new(Duration::ZERO));
        (
            Self {
                start: Instant::now(),
                waited: Arc::clone(&waited),
            },
            waited,
        )
    }
}

impl SinkClock for VirtualClock {
    fn now(&mut self) -> Instant {
        self.start + *self.waited.lock().unwrap()
    }

    fn wait(&mut self, interval: Duration) {
        *self.waited.lock().unwrap() += interval;
    }
}

/// A clock whose wait IS the reader draining one slot.
///
/// The slow-reader case needs a channel that becomes writable again between two
/// attempts. Arranging that with a real reader and a real pause is the race
/// this file was rewritten to remove, so the drain happens at the one moment
/// the sink is known to be between attempts: its own wait.
struct DrainingClock {
    /// Virtual time, so the retry is still counted.
    time: VirtualClock,
    /// The receiving half, drained one message per wait.
    receiver: Arc<Mutex<mpsc::Receiver<Message>>>,
    /// What the drain has taken out, in order, for the test to check.
    drained: Arc<Mutex<Vec<Message>>>,
}

impl SinkClock for DrainingClock {
    fn now(&mut self) -> Instant {
        self.time.now()
    }

    fn wait(&mut self, interval: Duration) {
        if let Ok(message) = self.receiver.lock().unwrap().try_recv() {
            self.drained.lock().unwrap().push(message);
        }
        self.time.wait(interval);
    }
}

/// Asserts a wait ended at the deadline: not before it, and not a whole retry
/// past it.
///
/// Both halves in one place, because only one of them was ever held. The
/// earlier stall test asserted `elapsed >= PATIENCE` alone, so multiplying the
/// deadline by a hundred left it green — the upper half of the bound, which is
/// the half the wedged-thread defect lives in, was observed by nothing.
fn assert_gave_up_at_the_deadline(waited: &Waited) {
    let waited = *waited.lock().unwrap();
    assert!(
        waited >= PATIENCE,
        "the sink must offer the message for the whole deadline: waited {waited:?} of {PATIENCE:?}"
    );
    assert!(
        waited < PATIENCE + RETRY_INTERVAL,
        "the sink must give up at the deadline, not past it: waited {waited:?}, and the deadline \
         can be overshot by at most one {RETRY_INTERVAL:?} retry"
    );
}

/// A sink whose deadline is measured on a fresh virtual clock.
fn stalled_sink(lines: mpsc::Sender<Message>) -> (StreamLogSink, Waited) {
    let (clock, waited) = VirtualClock::new();
    (
        StreamLogSink::with_patience_on_clock(lines, PATIENCE, Box::new(clock)),
        waited,
    )
}

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
    // blocking send would have parked a blocking-pool thread on forever. The
    // fullness is arranged by the capacity, not raced for.
    let (sender, _receiver) = mpsc::channel(1);
    let (mut sink, waited) = stalled_sink(sender);

    assert_eq!(sink.line("first", false), Ok(()));

    let outcome = sink.line("second", false);

    assert_eq!(
        outcome,
        Err(TailEnd::ClientStalled),
        "a client that stopped reading must be reported as dropped, not as closed"
    );
    assert_gave_up_at_the_deadline(&waited);
    assert!(
        TailEnd::ClientStalled.is_involuntary(),
        "the operator must be told about an ending the agent chose"
    );
}

#[tokio::test]
async fn a_slow_reader_is_waited_for_rather_than_dropped() {
    let (sender, receiver) = mpsc::channel(1);
    let (time, waited) = VirtualClock::new();
    let receiver = Arc::new(Mutex::new(receiver));
    let drained = Arc::new(Mutex::new(Vec::new()));
    let clock = DrainingClock {
        time,
        receiver: Arc::clone(&receiver),
        drained: Arc::clone(&drained),
    };
    let mut sink = StreamLogSink::with_patience_on_clock(sender, PATIENCE, Box::new(clock));

    assert_eq!(sink.line("first", false), Ok(()));

    // The reader takes its slot back during the sink's first retry wait: slow
    // is not absent, and nothing may be lost while the sink retries.
    let outcome = sink.line("second", false);
    assert_eq!(outcome, Ok(()), "a slow reader must be waited for");
    assert_eq!(
        *waited.lock().unwrap(),
        RETRY_INTERVAL,
        "one full channel costs exactly one retry, and the deadline is not touched"
    );

    let first = drained.lock().unwrap().remove(0);
    let second = receiver.lock().unwrap().try_recv().unwrap();
    for (response, expected) in [(first, "first"), (second, "second")] {
        match response.unwrap().result {
            Some(crate::proto::tail_site_log_response::Result::Ok(line)) => {
                assert_eq!(line.line, expected, "the retried line must not be lost");
            }
            other => panic!("expected a line, got {other:?}"),
        }
    }
}

#[tokio::test]
async fn a_terminal_message_for_a_client_that_never_drains_is_abandoned_at_the_deadline() {
    // The `TailEnd::ClientStalled` situation exactly: the channel is full, and
    // the receiver is alive so the channel is not closed. This is the path the
    // handler used to send the terminal message on with a plain
    // `blocking_send`, which has no deadline and does not return while the
    // client holds the stream open — see the witness below.
    let (sender, _receiver) = mpsc::channel(1);
    let (mut sink, waited) = stalled_sink(sender);
    assert_eq!(sink.line("first", false), Ok(()));

    sink.terminal(AgentError {
        code: ErrorCode::StreamDropped as i32,
        message: "the log stream was dropped".to_owned(),
        tool_output: String::new(),
    });

    // The lower half says the terminal message is really offered for the whole
    // deadline; the upper half says it is abandoned there rather than parking
    // the blocking-pool thread on a client that is not reading.
    assert_gave_up_at_the_deadline(&waited);
}

#[tokio::test]
async fn a_terminal_message_reaches_a_client_that_is_still_reading() {
    // The inverse control for the test above: a refusing bound that refused
    // everything would pass that one and be useless, so this proves the
    // terminal message is still DELIVERED when there is somewhere to put it.
    let (sender, mut receiver) = mpsc::channel(4);
    let (mut sink, waited) = stalled_sink(sender);

    sink.terminal(AgentError {
        code: ErrorCode::StreamIdle as i32,
        message: "the log stream was closed".to_owned(),
        tool_output: String::new(),
    });

    assert_eq!(
        *waited.lock().unwrap(),
        Duration::ZERO,
        "a channel with room must cost no wait at all"
    );
    match receiver.recv().await.unwrap().unwrap().result {
        Some(crate::proto::tail_site_log_response::Result::Error(error)) => {
            assert_eq!(error.code, ErrorCode::StreamIdle as i32);
        }
        other => panic!("the terminal message must arrive as an error payload, got {other:?}"),
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn a_plain_blocking_send_into_a_stalled_channel_does_not_return() {
    // The witness for why `StreamLogSink::terminal` exists rather than a second
    // clone of the sender. It measures tokio's own contract, not ours, and it
    // is kept because the defect it names is invisible in a diff: the two calls
    // read identically and only one of them has a deadline.
    //
    // The one wall-clock wait in this file, and it cannot answer wrongly under
    // load: a `blocking_send` into a full channel whose receiver is alive
    // CANNOT return, so no amount of scheduling delay turns the first assertion
    // false. The second is the synchronisation point that makes the first mean
    // something — the send completes only once a slot is freed, which proves
    // the thread was parked rather than finished, and reclaims it.
    let (sender, mut receiver) = mpsc::channel::<TailSiteLogResponse>(1);
    sender.try_send(TailSiteLogResponse::default()).unwrap();

    let (done_sender, done_receiver) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.blocking_send(TailSiteLogResponse::default());
        let _ = done_sender.send(());
    });

    assert!(
        done_receiver
            .recv_timeout(Duration::from_millis(200))
            .is_err(),
        "a plain blocking send must be shown to hang here, or the deadline above guards nothing"
    );

    receiver.recv().await.unwrap();
    done_receiver
        .recv_timeout(Duration::from_secs(60))
        .expect("the parked send must complete once a slot is freed, and the thread come back");
}
