//! The parent reads a trash listing from the child distrusting every byte.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::decode_trash_listing;
use crate::files::encode_trash_listing::encode_trash_listing;
use crate::files::model::entry_record_error::EntryRecordError;
use crate::files::model::trashed_entry::TrashedEntry;

fn entry(id: &str, original: &str, is_directory: bool) -> TrashedEntry {
    TrashedEntry {
        trash_id: id.to_owned(),
        original_path: original.to_owned(),
        trashed_at_unix: 1_700_000_000,
        is_directory,
        size_bytes: 7,
    }
}

#[test]
fn a_listing_survives_the_trip() {
    let sent = vec![entry("a", "x/y", true), entry("b", "", false)];

    assert_eq!(
        decode_trash_listing(&encode_trash_listing(&sent)).unwrap(),
        sent
    );
}

#[test]
fn an_empty_listing_survives_the_trip() {
    assert_eq!(
        decode_trash_listing(&encode_trash_listing(&[])).unwrap(),
        Vec::new()
    );
}

#[test]
fn a_truncated_answer_is_an_error_and_not_a_partial_listing() {
    let mut bytes = encode_trash_listing(&[entry("a", "x", false)]);
    bytes.pop();

    assert_eq!(
        decode_trash_listing(&bytes).unwrap_err(),
        EntryRecordError::Truncated
    );
}

#[test]
fn trailing_bytes_are_an_error() {
    let mut bytes = encode_trash_listing(&[]);
    bytes.push(0);

    assert_eq!(
        decode_trash_listing(&bytes).unwrap_err(),
        EntryRecordError::TrailingBytes
    );
}

#[test]
fn an_announced_count_above_the_ceiling_is_refused_before_anything_is_allocated() {
    let bytes = u32::MAX.to_le_bytes();

    assert_eq!(
        decode_trash_listing(&bytes).unwrap_err(),
        EntryRecordError::TooManyEntries
    );
}

#[test]
fn text_that_is_not_utf8_is_refused() {
    let mut bytes = vec![1, 0, 0, 0, 1, 0, 0xff];
    bytes.extend_from_slice(&[0, 0]);
    bytes.extend_from_slice(&0_i64.to_le_bytes());
    bytes.push(0);
    bytes.extend_from_slice(&0_u64.to_le_bytes());

    assert_eq!(
        decode_trash_listing(&bytes).unwrap_err(),
        EntryRecordError::NotUtf8
    );
}

#[test]
fn a_boolean_that_is_neither_zero_nor_one_is_refused() {
    let mut bytes = vec![1, 0, 0, 0, 0, 0, 0, 0];
    bytes.extend_from_slice(&0_i64.to_le_bytes());
    bytes.push(2);
    bytes.extend_from_slice(&0_u64.to_le_bytes());

    assert_eq!(
        decode_trash_listing(&bytes).unwrap_err(),
        EntryRecordError::UnknownTag
    );
}
