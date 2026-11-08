//! The bytes a child sends for a trash listing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::encode_trash_listing;
use crate::files::model::trashed_entry::TrashedEntry;

fn entry(id: &str, original: &str) -> TrashedEntry {
    TrashedEntry {
        trash_id: id.to_owned(),
        original_path: original.to_owned(),
        trashed_at_unix: 1_700_000_000,
        is_directory: true,
        size_bytes: 42,
    }
}

#[test]
fn an_empty_listing_is_just_a_zero_count() {
    assert_eq!(encode_trash_listing(&[]), vec![0, 0, 0, 0]);
}

#[test]
fn one_entry_is_its_count_its_two_texts_and_its_fixed_fields() {
    let bytes = encode_trash_listing(&[entry("id", "a/b")]);

    let mut expected = vec![1, 0, 0, 0];
    expected.extend_from_slice(&[2, 0, b'i', b'd']);
    expected.extend_from_slice(&[3, 0, b'a', b'/', b'b']);
    expected.extend_from_slice(&1_700_000_000_i64.to_le_bytes());
    expected.push(1);
    expected.extend_from_slice(&42_u64.to_le_bytes());
    assert_eq!(bytes, expected);
}
