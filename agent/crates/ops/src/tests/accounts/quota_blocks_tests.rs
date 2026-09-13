//! Tests for the `quota_blocks` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `quota_blocks.rs` declares this file
//! with `#[path]`, which keeps it a child module and therefore able to reach
//! private items.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::QuotaBlocks;

#[test]
fn bytes_are_rounded_up_to_the_next_block() {
    assert_eq!(QuotaBlocks::from_bytes(1025).as_argument(), "2");
    assert_eq!(QuotaBlocks::from_bytes(2048).as_argument(), "2");
}

#[test]
fn a_zero_quota_stays_zero_because_that_is_how_setquota_spells_unlimited() {
    assert_eq!(QuotaBlocks::from_bytes(0).as_argument(), "0");
}

#[test]
fn the_hard_limit_is_read_from_the_third_numeric_field() {
    let stdout = "/dev/sda1 100 4096 5120 0 0 0 0\n";

    let blocks = QuotaBlocks::parse_hard_limit(stdout).expect("a quota line is present");

    assert_eq!(blocks.to_bytes(), 5120 * 1024);
}

#[test]
fn an_exceeded_limit_marker_does_not_break_parsing() {
    let stdout = "/dev/sda1 6000* 4096 5120* 0 0 0 0\n";

    let blocks = QuotaBlocks::parse_hard_limit(stdout).expect("a quota line is present");

    assert_eq!(blocks.to_bytes(), 5120 * 1024);
}

#[test]
fn output_without_a_filesystem_line_means_no_limit() {
    assert_eq!(QuotaBlocks::parse_hard_limit(""), None);
    assert_eq!(
        QuotaBlocks::parse_hard_limit("Disk quotas for user acme\n"),
        None
    );
}
