//! Tests for the importer's view of the pump: an early end is the importer's abort.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::backup::{ImportInterrupted, ImportSource};
use tokio::sync::mpsc::channel;

use super::ChannelImportSource;
use crate::services::wire::chunk_receiver::ChunkReceiver;
use crate::services::wire::pump_message::PumpMessage;

#[test]
fn a_complete_stream_ends_cleanly_and_an_early_end_is_an_interruption() {
    let (sender, receiver) = channel(16);
    sender
        .try_send(PumpMessage::Chunk(b"one".to_vec()))
        .ok()
        .unwrap();
    sender.try_send(PumpMessage::Complete).ok().unwrap();
    let mut complete = ChannelImportSource::new(ChunkReceiver::new(receiver));

    assert_eq!(complete.next_chunk().unwrap(), Some(&b"one"[..]));
    assert_eq!(complete.next_chunk().unwrap(), None);

    let (sender, receiver) = channel(16);
    sender
        .try_send(PumpMessage::Chunk(b"partial".to_vec()))
        .ok()
        .unwrap();
    drop(sender);
    let mut cut = ChannelImportSource::new(ChunkReceiver::new(receiver));

    assert_eq!(cut.next_chunk().unwrap(), Some(&b"partial"[..]));
    assert_eq!(cut.next_chunk().unwrap_err(), ImportInterrupted);
}
