//! Tests for the bytes a dropped child hands the root parent.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{decode_listing, encode_listing};
use crate::files::model::entry_kind::EntryKind;
use crate::files::model::entry_record_error::EntryRecordError;
use crate::files::model::listed_entries::ListedEntries;
use crate::files::model::listed_entry::ListedEntry;
use crate::files::model::listing_answer::ListingAnswer;

fn entry(name: &str, kind: EntryKind) -> ListedEntry {
    ListedEntry {
        name: name.to_owned(),
        kind,
        size: 12_345,
        mode: 0o640,
        modified_unix: -42,
        uid: 1001,
        gid: 1002,
        link_count: 3,
    }
}

fn answer() -> ListingAnswer {
    ListingAnswer::Entries(ListedEntries {
        entries: vec![
            entry("index.php", EntryKind::File),
            entry("sites", EntryKind::Directory),
            entry("latest", EntryKind::Symlink),
            entry("socket", EntryKind::Other),
            entry("naïve-名前", EntryKind::File),
        ],
        skipped: 7,
    })
}

#[test]
fn a_listing_survives_the_round_trip_exactly() {
    assert_eq!(
        decode_listing(&encode_listing(&answer())).unwrap(),
        answer()
    );
}

#[test]
fn an_empty_listing_round_trips() {
    let empty = ListingAnswer::Entries(ListedEntries {
        entries: Vec::new(),
        skipped: 0,
    });

    assert_eq!(decode_listing(&encode_listing(&empty)).unwrap(), empty);
}

#[test]
fn a_too_large_refusal_round_trips_and_is_not_a_listing() {
    assert_eq!(
        decode_listing(&encode_listing(&ListingAnswer::TooLarge)).unwrap(),
        ListingAnswer::TooLarge
    );
}

#[test]
fn every_truncation_of_a_valid_answer_is_refused_and_never_partial() {
    let bytes = encode_listing(&answer());

    for cut in 0..bytes.len() {
        assert!(
            decode_listing(&bytes[..cut]).is_err(),
            "cut at {cut} was accepted"
        );
    }
}

#[test]
fn bytes_after_the_record_are_refused() {
    let mut bytes = encode_listing(&answer());
    bytes.push(0);

    assert_eq!(
        decode_listing(&bytes).unwrap_err(),
        EntryRecordError::TrailingBytes
    );
}

#[test]
fn an_unknown_answer_tag_is_refused() {
    assert_eq!(
        decode_listing(&[9]).unwrap_err(),
        EntryRecordError::UnknownTag
    );
}

#[test]
fn an_unknown_kind_tag_is_refused() {
    let mut bytes = encode_listing(&ListingAnswer::Entries(ListedEntries {
        entries: vec![entry("a", EntryKind::File)],
        skipped: 0,
    }));
    // tag(1) skipped(4) count(4) namelen(2) name(1) -> the kind byte.
    bytes[12] = 77;

    assert_eq!(
        decode_listing(&bytes).unwrap_err(),
        EntryRecordError::UnknownTag
    );
}

#[test]
fn a_count_beyond_the_ceiling_is_refused_before_anything_is_allocated() {
    let mut bytes = vec![0_u8];
    bytes.extend_from_slice(&0_u32.to_le_bytes());
    bytes.extend_from_slice(&100_001_u32.to_le_bytes());

    assert_eq!(
        decode_listing(&bytes).unwrap_err(),
        EntryRecordError::TooManyEntries
    );
    // Inverse control: the ceiling itself is announced without that error.
    let mut at_ceiling = vec![0_u8];
    at_ceiling.extend_from_slice(&0_u32.to_le_bytes());
    at_ceiling.extend_from_slice(&100_000_u32.to_le_bytes());
    assert_eq!(
        decode_listing(&at_ceiling).unwrap_err(),
        EntryRecordError::Truncated
    );
}

#[test]
fn a_name_longer_than_a_filesystem_allows_is_refused() {
    let mut bytes = vec![0_u8];
    bytes.extend_from_slice(&0_u32.to_le_bytes());
    bytes.extend_from_slice(&1_u32.to_le_bytes());
    bytes.extend_from_slice(&256_u16.to_le_bytes());
    bytes.extend_from_slice(&[b'a'; 256]);

    assert_eq!(
        decode_listing(&bytes).unwrap_err(),
        EntryRecordError::NameTooLong
    );
}

#[test]
fn a_name_that_is_not_utf8_is_refused() {
    let mut bytes = vec![0_u8];
    bytes.extend_from_slice(&0_u32.to_le_bytes());
    bytes.extend_from_slice(&1_u32.to_le_bytes());
    bytes.extend_from_slice(&1_u16.to_le_bytes());
    bytes.push(0xff);

    assert_eq!(
        decode_listing(&bytes).unwrap_err(),
        EntryRecordError::NotUtf8
    );
}
