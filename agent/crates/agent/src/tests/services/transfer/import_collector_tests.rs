//! The shape of an import stream: the header first and once, chunks after.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::proto::{ErrorCode, ImportBackupHeader};

fn header() -> ImportBackupRequest {
    ImportBackupRequest {
        part: Some(import_backup_request::Part::Header(ImportBackupHeader {
            manifest: b"m".to_vec(),
            signature: b"s".to_vec(),
            public_key: b"k".to_vec(),
            expected_key_fingerprint: "f".repeat(64),
            max_bytes: 9,
        })),
    }
}

fn chunk(bytes: &[u8]) -> ImportBackupRequest {
    ImportBackupRequest {
        part: Some(import_backup_request::Part::Chunk(bytes.to_vec())),
    }
}

#[test]
fn the_header_then_chunks_are_accepted_in_order() {
    let mut collector = ImportCollector::new();

    let opened = collector.accept(header()).unwrap();
    let request = opened.header.unwrap();
    assert_eq!(request.signed.manifest, b"m");
    assert_eq!(request.signed.signature, b"s");
    assert_eq!(request.signed.public_key, b"k");
    assert_eq!(request.expected_fingerprint, "f".repeat(64));
    assert_eq!(request.max_bytes, 9);
    assert!(opened.chunk.is_empty());

    let next = collector.accept(chunk(b"abc")).unwrap();
    assert!(next.header.is_none());
    assert_eq!(next.chunk, b"abc");
}

#[test]
fn a_stream_that_starts_with_a_chunk_is_refused() {
    let error = ImportCollector::new().accept(chunk(b"abc")).err().unwrap();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_message_with_no_part_is_refused_at_either_position() {
    let empty = || ImportBackupRequest { part: None };

    assert!(ImportCollector::new().accept(empty()).is_err());
    let mut collector = ImportCollector::new();
    collector.accept(header()).unwrap();
    assert!(collector.accept(empty()).is_err());
}

#[test]
fn a_second_header_is_refused() {
    let mut collector = ImportCollector::new();
    collector.accept(header()).unwrap();

    let error = collector.accept(header()).err().unwrap();
    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
