//! Tests for reading a search child's bytes back, and for refusing hostile ones.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::entry_kind::EntryKind;

use super::decode_search_outcome;
use crate::files::encode_search_outcome::encode_search_outcome;
use crate::files::model::entry_record_error::EntryRecordError;
use crate::files::model::search_match::SearchMatch;
use crate::files::model::search_outcome::SearchOutcome;

/// A match named `path`.
fn found(path: &str, kind: EntryKind) -> SearchMatch {
    SearchMatch {
        path: path.to_owned(),
        kind,
        size: 42,
        mode: 0o640,
        modified_unix: 1_700_000_000,
        uid: 1000,
        gid: 1001,
        links: 1,
    }
}

#[test]
fn an_encoded_outcome_decodes_to_the_same_outcome() {
    let outcome = SearchOutcome {
        matches: vec![
            found("a/b.txt", EntryKind::File),
            found("a/dir", EntryKind::Directory),
            found("a/link", EntryKind::Symlink),
            found("a/fifo", EntryKind::Other),
        ],
        truncated: true,
        skipped_entries: 3,
    };

    assert_eq!(
        decode_search_outcome(&encode_search_outcome(&outcome)).unwrap(),
        outcome
    );
}

#[test]
fn an_outcome_with_no_matches_round_trips() {
    let outcome = SearchOutcome::default();

    assert_eq!(
        decode_search_outcome(&encode_search_outcome(&outcome)).unwrap(),
        outcome
    );
}

#[test]
fn every_prefix_of_a_valid_encoding_is_refused_as_truncated() {
    let bytes = encode_search_outcome(&SearchOutcome {
        matches: vec![found("a/b.txt", EntryKind::File)],
        truncated: false,
        skipped_entries: 0,
    });

    for length in 0..bytes.len() {
        assert_eq!(
            decode_search_outcome(&bytes[..length]).unwrap_err(),
            EntryRecordError::Truncated,
            "a prefix of {length} bytes must be refused"
        );
    }
}

#[test]
fn trailing_bytes_are_refused() {
    let mut bytes = encode_search_outcome(&SearchOutcome::default());
    bytes.push(0);

    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::TrailingBytes
    );
}

#[test]
fn a_count_above_five_hundred_is_refused_before_anything_is_allocated() {
    let mut bytes = vec![0, 0, 0, 0, 0];
    bytes.extend_from_slice(&501_u32.to_le_bytes());

    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::TooManyEntries
    );
}

#[test]
fn a_count_of_exactly_five_hundred_is_not_refused_for_its_size() {
    let mut bytes = vec![0, 0, 0, 0, 0];
    bytes.extend_from_slice(&500_u32.to_le_bytes());

    // No matches follow, so it fails for truncation, which proves the count passed.
    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::Truncated
    );
}

#[test]
fn a_path_length_above_its_bound_is_refused() {
    let mut bytes = vec![0, 0, 0, 0, 0, 1, 0, 0, 0];
    bytes.extend_from_slice(&(32 * 1024 + 1_u32).to_le_bytes());

    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::NameTooLong
    );
}

#[test]
fn a_path_that_is_not_utf8_is_refused() {
    let mut bytes = vec![0, 0, 0, 0, 0, 1, 0, 0, 0];
    bytes.extend_from_slice(&1_u32.to_le_bytes());
    bytes.push(0xff);

    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::NotUtf8
    );
}

#[test]
fn a_truncated_flag_other_than_zero_or_one_is_refused() {
    assert_eq!(
        decode_search_outcome(&[2, 0, 0, 0, 0, 0, 0, 0, 0]).unwrap_err(),
        EntryRecordError::UnknownTag
    );
}

#[test]
fn an_unknown_kind_byte_is_refused() {
    let mut bytes = encode_search_outcome(&SearchOutcome {
        matches: vec![found("x", EntryKind::File)],
        truncated: false,
        skipped_entries: 0,
    });
    // Header 9, length 4, path 1: the kind byte is next.
    bytes[14] = 9;

    assert_eq!(
        decode_search_outcome(&bytes).unwrap_err(),
        EntryRecordError::UnknownTag
    );
}
