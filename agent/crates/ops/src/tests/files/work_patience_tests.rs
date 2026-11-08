//! How long the parent waits for a file-manager child.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use super::WorkPatience;

#[test]
fn a_single_entry_gets_two_minutes() {
    assert_eq!(WorkPatience::Single.duration(), Duration::from_secs(120));
}

#[test]
fn a_tree_gets_ten_minutes() {
    assert_eq!(WorkPatience::Bulk.duration(), Duration::from_secs(600));
}
