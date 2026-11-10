//! Tests for [`parse_mailbox_count`]: reading the number of mailboxes under a home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::parse_mailbox_count::parse_mailbox_count;

/// A plain count, and one padded with spaces the way some `wc` prints it.
#[test]
fn a_count_is_read_with_or_without_padding() {
    assert_eq!(parse_mailbox_count("3\n"), Some(3));
    assert_eq!(parse_mailbox_count("      12\n"), Some(12));
    assert_eq!(parse_mailbox_count("0\n"), Some(0));
}

/// Anything else is not measured.
#[test]
fn anything_else_is_not_measured() {
    assert_eq!(parse_mailbox_count(""), None);
    assert_eq!(parse_mailbox_count("many\n"), None);
    assert_eq!(parse_mailbox_count("-1\n"), None);
    assert_eq!(parse_mailbox_count("3 4\n"), None);
    assert_eq!(parse_mailbox_count("4294967296\n"), None);
}

/// The most believable count is the inverse control for the ceiling above it.
#[test]
fn the_ceiling_is_held() {
    assert_eq!(parse_mailbox_count("10000000\n"), Some(10_000_000));
    assert_eq!(parse_mailbox_count("10000001\n"), None);
}
