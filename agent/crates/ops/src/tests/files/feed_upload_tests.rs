//! Tests for the parent's half of an upload: chunks sent on, the cap counted, aborts refused.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io;

use super::feed_upload;
use crate::files::feed_stop::FeedStop;
use crate::files::model::buffered_upload::BufferedUpload;
use crate::files::model::upload_aborted::UploadAborted;
use crate::files::model::upload_source::UploadSource;

/// A source that breaks before it finishes.
struct BrokenUpload;

impl UploadSource for BrokenUpload {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        Err(UploadAborted)
    }
}

/// Runs the feed collecting what was sent.
fn run(source: &mut dyn UploadSource, limit: u64) -> (Result<(), FeedStop>, Vec<u8>) {
    let mut sent = Vec::new();
    let result = feed_upload(source, limit, &mut |chunk: &[u8]| {
        sent.extend_from_slice(chunk);
        Ok(())
    });
    (result, sent)
}

#[test]
fn every_byte_is_sent_when_the_source_completes() {
    let (result, sent) = run(&mut BufferedUpload::new(b"hello".to_vec()), 1024);

    assert!(result.is_ok());
    assert_eq!(sent, b"hello");
}

#[test]
fn an_empty_upload_completes_having_sent_nothing() {
    let (result, sent) = run(&mut BufferedUpload::new(Vec::new()), 1024);

    assert!(result.is_ok());
    assert!(sent.is_empty());
}

#[test]
fn a_body_of_exactly_the_cap_is_sent() {
    let (result, sent) = run(&mut BufferedUpload::new(vec![7; 10]), 10);

    assert!(result.is_ok());
    assert_eq!(sent.len(), 10);
}

#[test]
fn a_body_one_byte_over_the_cap_is_refused_and_none_of_it_is_sent() {
    let (result, sent) = run(&mut BufferedUpload::new(vec![7; 11]), 10);

    assert!(matches!(result, Err(FeedStop::OverLimit)));
    assert!(sent.is_empty());
}

#[test]
fn a_source_that_breaks_is_an_abort() {
    let (result, _) = run(&mut BrokenUpload, 10);

    assert!(matches!(result, Err(FeedStop::Aborted)));
}

#[test]
fn a_channel_failure_is_reported_as_one_and_stops_the_feed() {
    let result = feed_upload(
        &mut BufferedUpload::new(b"x".to_vec()),
        10,
        &mut |_: &[u8]| Err(io::Error::from_raw_os_error(libc::EPIPE)),
    );

    match result {
        Err(FeedStop::Channel(error)) => assert_eq!(error.raw_os_error(), Some(libc::EPIPE)),
        other => panic!("expected a channel failure, got {other:?}"),
    }
}
