//! Tests for `StreamChunkSink`: chunks arrive as ok payloads, and the sink
//! remembers WHY it stopped so the handler can tell a closed client from a
//! stalled one. Deadlines run on an injected virtual clock; nothing sleeps.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use maran_ops::files::model::chunk_sink::ChunkSink;
use tokio::sync::mpsc;
use tonic::Status;

use super::StreamChunkSink;
use crate::proto::{AgentError, ErrorCode, ReadFileResponse, read_file_response};
use crate::services::wire::send_end::SendEnd;
use crate::services::wire::sink_clock::SinkClock;

/// One message as it travels down the stream's channel.
type Message = Result<ReadFileResponse, Status>;

/// A clock on virtual time that never sleeps.
struct VirtualClock {
    /// The instant virtual time started from.
    start: Instant,
    /// How far virtual time has advanced.
    waited: Arc<Mutex<Duration>>,
}

impl SinkClock for VirtualClock {
    fn now(&mut self) -> Instant {
        self.start + *self.waited.lock().unwrap()
    }

    fn wait(&mut self, interval: Duration) {
        *self.waited.lock().unwrap() += interval;
    }
}

/// A sink with a 5 s patience on virtual time.
fn sink_on_virtual_time(channel: mpsc::Sender<Message>) -> StreamChunkSink {
    let clock = VirtualClock {
        start: Instant::now(),
        waited: Arc::new(Mutex::new(Duration::ZERO)),
    };
    StreamChunkSink::with_patience_on_clock(channel, Duration::from_secs(5), Box::new(clock))
}

#[tokio::test]
async fn a_chunk_arrives_as_an_ok_payload_and_leaves_no_ending() {
    let (channel, mut receiver) = mpsc::channel(4);
    let mut sink = StreamChunkSink::new(channel);

    assert!(sink.send(b"hello").is_ok());

    match receiver.recv().await.unwrap().unwrap().result {
        Some(read_file_response::Result::Ok(chunk)) => assert_eq!(chunk.data, b"hello"),
        other => panic!("expected a chunk, got {other:?}"),
    }
    assert_eq!(sink.ending(), None);
}

#[tokio::test]
async fn a_closed_stream_stops_the_sink_with_a_closed_ending() {
    let (channel, receiver) = mpsc::channel(4);
    drop(receiver);
    let mut sink = StreamChunkSink::new(channel);

    assert!(sink.send(b"x").is_err());
    assert_eq!(sink.ending(), Some(SendEnd::Closed));
}

#[tokio::test]
async fn a_client_that_never_drains_stalls_the_sink_with_a_stalled_ending() {
    let (channel, _receiver) = mpsc::channel(1);
    let mut sink = sink_on_virtual_time(channel);
    assert!(sink.send(b"first").is_ok());

    assert!(sink.send(b"second").is_err());

    assert_eq!(sink.ending(), Some(SendEnd::Stalled));
}

#[tokio::test]
async fn a_terminal_error_arrives_as_an_error_payload() {
    let (channel, mut receiver) = mpsc::channel(4);
    let mut sink = StreamChunkSink::new(channel);

    sink.terminal(AgentError {
        code: ErrorCode::TooLarge as i32,
        message: "too large".to_owned(),
        tool_output: String::new(),
    });

    match receiver.recv().await.unwrap().unwrap().result {
        Some(read_file_response::Result::Error(error)) => {
            assert_eq!(error.code, ErrorCode::TooLarge as i32);
        }
        other => panic!("expected an error payload, got {other:?}"),
    }
}

#[tokio::test]
async fn a_terminal_error_to_a_client_that_never_drains_is_abandoned_and_returns() {
    let (channel, _receiver) = mpsc::channel(1);
    let mut sink = sink_on_virtual_time(channel);
    assert!(sink.send(b"first").is_ok());

    // Returning at all is the assertion: on a virtual clock the call ends at
    // the deadline, where a plain blocking send would not return.
    sink.terminal(AgentError {
        code: ErrorCode::StreamDropped as i32,
        message: "dropped".to_owned(),
        tool_output: String::new(),
    });
}
