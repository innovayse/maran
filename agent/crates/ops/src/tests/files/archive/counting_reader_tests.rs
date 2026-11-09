//! Tests for the bound under a decompressed stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Cursor, ErrorKind, Read};

use super::CountingReader;

#[test]
fn a_stream_exactly_at_the_limit_is_delivered_whole() {
    let mut reader = CountingReader::new(Cursor::new(vec![7_u8; 100]), 100);
    let mut out = Vec::new();

    reader.read_to_end(&mut out).unwrap();

    assert_eq!(out.len(), 100);
}

#[test]
fn a_stream_one_byte_over_the_limit_fails_instead_of_ending() {
    let mut reader = CountingReader::new(Cursor::new(vec![7_u8; 101]), 100);
    let mut out = Vec::new();

    let error = reader.read_to_end(&mut out).unwrap_err();

    assert_eq!(error.kind(), ErrorKind::FileTooLarge);
}

#[test]
fn an_endless_stream_is_cut_at_the_limit() {
    let mut reader = CountingReader::new(std::io::repeat(0), 1_000_000);
    let mut sink = std::io::sink();

    let error = std::io::copy(&mut reader, &mut sink).unwrap_err();

    assert_eq!(error.kind(), ErrorKind::FileTooLarge);
}

#[test]
fn an_empty_stream_is_empty() {
    let mut reader = CountingReader::new(Cursor::new(Vec::new()), 0);
    let mut out = Vec::new();

    reader.read_to_end(&mut out).unwrap();

    assert!(out.is_empty());
}
