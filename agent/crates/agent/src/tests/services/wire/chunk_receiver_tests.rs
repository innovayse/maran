//! Tests for the blocking side of a pump: complete only with the terminator.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tokio::sync::mpsc::channel;

use super::ChunkReceiver;
use crate::services::wire::pump_message::PumpMessage;
use crate::services::wire::stream_ended::StreamEnded;

/// A receiver fed the messages given, then closed.
fn receiver_of(messages: Vec<PumpMessage>) -> ChunkReceiver {
    let (sender, receiver) = channel(16);
    for message in messages {
        sender.try_send(message).ok().unwrap();
    }
    ChunkReceiver::new(receiver)
}

#[test]
fn chunks_arrive_in_order_and_the_terminator_completes_the_stream() {
    let mut receiver = receiver_of(vec![
        PumpMessage::Chunk(b"one".to_vec()),
        PumpMessage::Chunk(b"two".to_vec()),
        PumpMessage::Complete,
    ]);

    assert_eq!(receiver.next_chunk().unwrap(), Some(&b"one"[..]));
    assert_eq!(receiver.next_chunk().unwrap(), Some(&b"two"[..]));
    assert_eq!(receiver.next_chunk().unwrap(), None);
    assert_eq!(receiver.next_chunk().unwrap(), None);
}

#[test]
fn a_channel_that_closes_without_the_terminator_has_ended_early_not_complete() {
    let mut receiver = receiver_of(vec![PumpMessage::Chunk(b"partial".to_vec())]);

    assert_eq!(receiver.next_chunk().unwrap(), Some(&b"partial"[..]));
    assert_eq!(receiver.next_chunk().unwrap_err(), StreamEnded);
}

#[test]
fn an_empty_chunk_is_skipped_and_never_reported_as_the_end() {
    let mut receiver = receiver_of(vec![
        PumpMessage::Chunk(Vec::new()),
        PumpMessage::Chunk(b"x".to_vec()),
        PumpMessage::Complete,
    ]);

    assert_eq!(receiver.next_chunk().unwrap(), Some(&b"x"[..]));
    assert_eq!(receiver.next_chunk().unwrap(), None);
}

#[test]
fn a_stream_with_only_the_terminator_is_an_empty_complete_stream() {
    let mut receiver = receiver_of(vec![PumpMessage::Complete]);

    assert_eq!(receiver.next_chunk().unwrap(), None);
}
