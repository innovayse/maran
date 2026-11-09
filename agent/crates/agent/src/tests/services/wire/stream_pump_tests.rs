//! Tests for how a pump's refusal is read back.
//!
//! A `tonic::Streaming` cannot be built by a unit test, so the collectors' decisions are
//! covered by the collectors' tests and the transport by the handshake over a real socket.
//! What is decided here is `finish`, and, over a channel-backed stream standing in for the
//! transport, the idle limit: a client that goes quiet is refused and lets go of the lock the
//! blocking operation holds, and a slow but steady client is not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::Duration;

use tokio::sync::mpsc::channel;
use tokio_stream::wrappers::ReceiverStream;
use tonic::Status;

use super::{CHUNK_IDLE_LIMIT, StreamPump};
use crate::proto::{AgentError, ErrorCode};
use crate::services::wire::chunk_collector::ChunkCollector;
use crate::services::wire::chunk_receiver::ChunkReceiver;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::stream_ended::StreamEnded;

/// A collector that accepts every message as it is.
struct PassThrough;

impl ChunkCollector<Vec<u8>> for PassThrough {
    fn accept_chunk(&mut self, message: Vec<u8>) -> Result<Vec<u8>, AgentError> {
        Ok(message)
    }
}

/// The blocking side of an upload: takes the "account lock", then reads chunks until the
/// stream ends. Answers the bytes it received, or [`StreamEnded`] for a stream cut short,
/// and releases the lock by returning.
fn hold_lock_while_reading(
    lock: Arc<Mutex<()>>,
    mut chunks: ChunkReceiver,
) -> Result<Vec<u8>, StreamEnded> {
    let _held = lock.lock().unwrap();
    let mut received = Vec::new();
    while let Some(chunk) = chunks.next_chunk()? {
        received.extend_from_slice(chunk);
    }
    Ok(received)
}

/// Runs the blocking side on a plain thread and answers its result.
///
/// Not `spawn_blocking`: while a blocking-pool task is outstanding the paused test
/// clock does not advance on its own, so the pump's thirty-second limit would be waited
/// out in real time. A plain thread is what the production blocking pool is to the pump,
/// a thread reading a channel, without that effect on the clock.
fn run_on_thread<T: Send + 'static>(
    work: impl FnOnce() -> T + Send + 'static,
) -> tokio::sync::oneshot::Receiver<T> {
    let (done, result) = tokio::sync::oneshot::channel();
    std::thread::spawn(move || {
        let _ = done.send(work());
    });
    result
}

/// A pump that stopped with `refusal` (or did not stop at all).
fn pump(refusal: Option<AgentError>) -> StreamPump {
    let task = tokio::spawn(std::future::pending::<()>());
    StreamPump {
        refusal: Arc::new(Mutex::new(refusal)),
        task: task.abort_handle(),
    }
}

#[tokio::test]
async fn the_refusal_a_pump_stopped_with_is_handed_back() {
    let refusal = pump(Some(invalid_input("over the cap".to_owned())))
        .finish()
        .unwrap();

    assert_eq!(refusal.code, ErrorCode::InvalidInput as i32);
}

#[tokio::test]
async fn a_pump_that_did_not_refuse_hands_back_nothing() {
    assert!(pump(None).finish().is_none());
}

#[tokio::test]
async fn finishing_stops_a_pump_that_is_still_waiting_on_a_stalled_client() {
    let task = tokio::spawn(std::future::pending::<()>());
    let handle = task.abort_handle();
    let pump = StreamPump {
        refusal: Arc::new(Mutex::new(None)),
        task: handle,
    };

    pump.finish();

    assert!(task.await.unwrap_err().is_cancelled());
}

#[tokio::test(start_paused = true)]
async fn a_client_that_stops_sending_without_closing_is_refused_and_the_lock_is_released() {
    let (sender, receiver) = channel::<Result<Vec<u8>, Status>>(4);
    let (chunks, pump) = StreamPump::start(
        ReceiverStream::new(receiver),
        PassThrough,
        Vec::new(),
        "write",
    );
    let lock = Arc::new(Mutex::new(()));
    let operation = run_on_thread({
        let lock = Arc::clone(&lock);
        move || hold_lock_while_reading(lock, chunks)
    });
    sender.send(Ok(b"first".to_vec())).await.unwrap();

    // The sender stays open and silent: the stream is neither finished nor closed.
    let outcome = operation.await.unwrap();

    assert_eq!(outcome.unwrap_err(), StreamEnded);
    assert!(
        lock.try_lock().is_ok(),
        "the lock must be free once the operation returned"
    );
    let refusal = pump.finish().unwrap();
    assert_eq!(refusal.code, ErrorCode::StreamDropped as i32);
}

#[tokio::test(start_paused = true)]
async fn a_steady_stream_slower_than_the_limit_overall_is_accepted() {
    let (sender, receiver) = channel::<Result<Vec<u8>, Status>>(4);
    let (chunks, pump) = StreamPump::start(
        ReceiverStream::new(receiver),
        PassThrough,
        Vec::new(),
        "write",
    );
    let lock = Arc::new(Mutex::new(()));
    let operation = run_on_thread({
        let lock = Arc::clone(&lock);
        move || hold_lock_while_reading(lock, chunks)
    });

    // Three gaps, each shorter than the limit, together longer than it: the limit is
    // per message and not per stream.
    let gap = CHUNK_IDLE_LIMIT - Duration::from_secs(5);
    for part in [&b"a"[..], b"b", b"c"] {
        tokio::time::sleep(gap).await;
        sender.send(Ok(part.to_vec())).await.unwrap();
    }
    drop(sender);

    assert_eq!(operation.await.unwrap().unwrap(), b"abc".to_vec());
    assert!(pump.finish().is_none());
}
