//! Tests for pumping an opened file into a sink under a byte limit.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::Write;

use super::stream_opened_file;
use crate::files::FilesOpError;
use crate::files::model::chunk_sink::ChunkSink;
use crate::files::model::sink_stopped::SinkStopped;

/// Keeps every chunk, and optionally stops after a number of them.
struct Recording {
    /// What arrived, concatenated.
    bytes: Vec<u8>,
    /// How many chunks arrived.
    chunks: usize,
    /// Stop answering after this many chunks.
    stop_after: Option<usize>,
}

impl Recording {
    /// A sink that never stops.
    fn open() -> Self {
        Self {
            bytes: Vec::new(),
            chunks: 0,
            stop_after: None,
        }
    }
}

impl ChunkSink for Recording {
    fn send(&mut self, chunk: &[u8]) -> Result<(), SinkStopped> {
        if self.stop_after == Some(self.chunks) {
            return Err(SinkStopped);
        }
        self.chunks += 1;
        self.bytes.extend_from_slice(chunk);
        Ok(())
    }
}

/// A temporary file holding `content`, opened for reading.
fn file_with(content: &[u8]) -> (tempfile::NamedTempFile, File) {
    let mut written = tempfile::NamedTempFile::new().unwrap();
    written.write_all(content).unwrap();
    let opened = File::open(written.path()).unwrap();

    (written, opened)
}

#[test]
fn a_small_file_arrives_byte_exact_in_one_chunk() {
    let (_keep, file) = file_with(b"hello");
    let mut sink = Recording::open();

    let sent = stream_opened_file(file, 100, &mut sink).unwrap();

    assert_eq!(sent, 5);
    assert_eq!(sink.bytes, b"hello");
    assert_eq!(sink.chunks, 1);
}

#[test]
fn a_file_larger_than_a_chunk_arrives_byte_exact_in_several() {
    let content: Vec<u8> = (0..200_000_u32).map(|n| (n % 251) as u8).collect();
    let (_keep, file) = file_with(&content);
    let mut sink = Recording::open();

    let sent = stream_opened_file(file, 1_000_000, &mut sink).unwrap();

    assert_eq!(sent, 200_000);
    assert_eq!(sink.bytes, content);
    assert!(
        sink.chunks >= 4,
        "200 000 bytes need at least four 64 KiB chunks"
    );
}

#[test]
fn an_empty_file_sends_nothing_and_succeeds() {
    let (_keep, file) = file_with(b"");
    let mut sink = Recording::open();

    assert_eq!(stream_opened_file(file, 10, &mut sink).unwrap(), 0);
    assert_eq!(sink.chunks, 0);
}

#[test]
fn a_file_exactly_at_the_limit_is_sent_whole() {
    let (_keep, file) = file_with(b"12345");
    let mut sink = Recording::open();

    assert_eq!(stream_opened_file(file, 5, &mut sink).unwrap(), 5);
    assert_eq!(sink.bytes, b"12345");
}

#[test]
fn a_file_one_byte_over_the_limit_is_refused_before_any_byte_is_sent() {
    let (_keep, file) = file_with(b"123456");
    let mut sink = Recording::open();

    assert_eq!(
        stream_opened_file(file, 5, &mut sink).unwrap_err(),
        FilesOpError::TooLarge
    );
    assert_eq!(sink.chunks, 0);
}

#[test]
fn bytes_beyond_the_limit_that_the_size_did_not_announce_end_in_too_large() {
    // A procfs file reports a size of 0 and then yields content: the same shape
    // as a customer filling a file after the size check. Only the count of the
    // bytes that actually stream can refuse it.
    let file = File::open("/proc/self/status").unwrap();
    assert_eq!(
        file.metadata().unwrap().len(),
        0,
        "the fixture must announce no size"
    );
    let mut sink = Recording::open();

    assert_eq!(
        stream_opened_file(file, 10, &mut sink).unwrap_err(),
        FilesOpError::TooLarge
    );
    assert!(sink.bytes.len() <= 10, "nothing past the limit may be sent");
}

#[test]
fn the_same_unannounced_file_streams_when_the_limit_is_large_enough() {
    let file = File::open("/proc/self/status").unwrap();
    let mut sink = Recording::open();

    let sent = stream_opened_file(file, 10_000_000, &mut sink).unwrap();

    assert!(sent > 10);
    assert_eq!(sent as usize, sink.bytes.len());
}

#[test]
fn a_sink_that_stops_ends_the_read_with_what_was_sent() {
    let content = vec![7_u8; 200_000];
    let (_keep, file) = file_with(&content);
    let mut sink = Recording {
        bytes: Vec::new(),
        chunks: 0,
        stop_after: Some(1),
    };

    let sent = stream_opened_file(file, 1_000_000, &mut sink).unwrap();

    assert_eq!(sent, 65_536);
    assert_eq!(sink.chunks, 1);
}

#[test]
fn a_directory_is_not_a_regular_file() {
    let directory = tempfile::tempdir().unwrap();
    let file = File::open(directory.path()).unwrap();
    let mut sink = Recording::open();

    assert_eq!(
        stream_opened_file(file, 100, &mut sink).unwrap_err(),
        FilesOpError::NotARegularFile
    );
}

#[test]
fn a_file_with_two_links_is_refused_by_the_parent_check_alone() {
    let (keep, _file) = file_with(b"data");
    let alias = keep.path().with_extension("alias");
    std::fs::hard_link(keep.path(), &alias).unwrap();
    let file = File::open(keep.path()).unwrap();
    let mut sink = Recording::open();

    let refused = stream_opened_file(file, 100, &mut sink).unwrap_err();
    std::fs::remove_file(&alias).unwrap();

    assert_eq!(refused, FilesOpError::NotARegularFile);
    assert_eq!(sink.chunks, 0);
}
