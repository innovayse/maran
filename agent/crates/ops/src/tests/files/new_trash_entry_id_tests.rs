//! The identifier of a trashed entry has the shape the listing accepts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::new_trash_entry_id;

#[test]
fn the_identifier_is_the_seconds_a_dash_and_sixteen_hex_digits() {
    let id = new_trash_entry_id(1_700_000_000, 0xabc).unwrap();

    assert_eq!(id.as_str(), "1700000000-0000000000000abc");
}

#[test]
fn the_identifier_remembers_when_it_was_made() {
    let id = new_trash_entry_id(1_700_000_000, 7).unwrap();

    assert_eq!(id.unix_seconds(), 1_700_000_000);
}
