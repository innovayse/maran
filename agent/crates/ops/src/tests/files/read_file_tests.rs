//! Tests for the read operation over a host that hands back a prepared file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::Write;

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use super::read_file;
use crate::files::FilesOpError;
use crate::files::browse_host::BrowseHost;
use crate::files::model::chunk_sink::ChunkSink;
use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::listing_window::ListingWindow;
use crate::files::model::opened_for_read::OpenedForRead;
use crate::files::model::read_file_input::ReadFileInput;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;
use crate::files::model::sink_stopped::SinkStopped;

/// A host whose open answers with a file at `path`, or a refusal.
struct OneFileHost {
    /// The file to open, or the refusal to give.
    answer: std::sync::Mutex<Option<Result<std::path::PathBuf, FilesOpError>>>,
    /// The hold handed out with the file; its strong count shows when it is released.
    hold: std::sync::Arc<()>,
}

impl BrowseHost for OneFileHost {
    fn list_as_account(
        &self,
        _account: &AccountName,
        _path: &BrowsePath,
        _window: &ListingWindow,
    ) -> Result<DirectoryListing, FilesOpError> {
        Err(FilesOpError::NotFound)
    }

    fn open_for_read_as_account(
        &self,
        _account: &AccountName,
        _path: &BrowsePath,
    ) -> Result<OpenedForRead, FilesOpError> {
        match self.answer.lock().unwrap().take().unwrap() {
            Ok(path) => Ok(OpenedForRead::new(
                File::open(path).unwrap(),
                self.hold.clone(),
            )),
            Err(error) => Err(error),
        }
    }

    fn search_as_account(
        &self,
        _account: &AccountName,
        _input: &SearchInput,
    ) -> Result<SearchOutcome, FilesOpError> {
        Err(FilesOpError::NotFound)
    }
}

/// Collects what arrives.
struct Collect(Vec<u8>);

impl ChunkSink for Collect {
    fn send(&mut self, chunk: &[u8]) -> Result<(), SinkStopped> {
        self.0.extend_from_slice(chunk);
        Ok(())
    }
}

/// A request to read any path with `limit`.
fn request(limit: u64) -> ReadFileInput {
    ReadFileInput::new(
        AccountName::parse("alice1").unwrap(),
        BrowsePath::parse("a.txt").unwrap(),
        limit,
    )
    .unwrap()
}

#[test]
fn the_file_the_host_opens_is_streamed_to_the_sink() {
    let mut written = tempfile::NamedTempFile::new().unwrap();
    written.write_all(b"content").unwrap();
    let host = OneFileHost {
        hold: std::sync::Arc::new(()),
        answer: std::sync::Mutex::new(Some(Ok(written.path().to_path_buf()))),
    };
    let mut sink = Collect(Vec::new());

    let sent = read_file(&host, &request(100), &mut sink).unwrap();

    assert_eq!(sent, 7);
    assert_eq!(sink.0, b"content");
}

#[test]
fn the_limit_of_the_request_bounds_the_stream() {
    let mut written = tempfile::NamedTempFile::new().unwrap();
    written.write_all(b"content").unwrap();
    let host = OneFileHost {
        hold: std::sync::Arc::new(()),
        answer: std::sync::Mutex::new(Some(Ok(written.path().to_path_buf()))),
    };
    let mut sink = Collect(Vec::new());

    assert_eq!(
        read_file(&host, &request(3), &mut sink).unwrap_err(),
        FilesOpError::TooLarge
    );
    assert!(sink.0.is_empty());
}

#[test]
fn a_refusal_of_the_host_reaches_the_caller_and_nothing_is_sent() {
    let host = OneFileHost {
        hold: std::sync::Arc::new(()),
        answer: std::sync::Mutex::new(Some(Err(FilesOpError::Saturated))),
    };
    let mut sink = Collect(Vec::new());

    assert_eq!(
        read_file(&host, &request(100), &mut sink).unwrap_err(),
        FilesOpError::Saturated
    );
    assert!(sink.0.is_empty());
}

#[test]
fn the_hold_travels_with_the_file_through_the_whole_stream_and_is_released_after() {
    /// Observes how many holds are alive at each chunk.
    struct Watch<'a>(&'a std::sync::Arc<()>, Vec<usize>);

    impl ChunkSink for Watch<'_> {
        fn send(&mut self, _chunk: &[u8]) -> Result<(), SinkStopped> {
            self.1.push(std::sync::Arc::strong_count(self.0));
            Ok(())
        }
    }

    let mut written = tempfile::NamedTempFile::new().unwrap();
    written.write_all(&vec![1_u8; 200_000]).unwrap();
    let host = OneFileHost {
        hold: std::sync::Arc::new(()),
        answer: std::sync::Mutex::new(Some(Ok(written.path().to_path_buf()))),
    };
    let mut sink = Watch(&host.hold, Vec::new());

    read_file(&host, &request(1_000_000), &mut sink).unwrap();

    assert!(sink.1.len() >= 4);
    assert!(
        sink.1.iter().all(|&alive| alive == 2),
        "held while every chunk is sent"
    );
    assert_eq!(
        std::sync::Arc::strong_count(&host.hold),
        1,
        "released when the read ends"
    );
}
