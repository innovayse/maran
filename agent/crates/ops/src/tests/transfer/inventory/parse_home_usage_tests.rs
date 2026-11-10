//! Tests for [`parse_home_usage`]: reading the size of one home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::parse_home_usage::parse_home_usage;

/// Kibibytes become bytes.
#[test]
fn kibibytes_become_bytes() {
    assert_eq!(parse_home_usage("2048\t/home/acme\n"), Some(2_097_152));
    assert_eq!(parse_home_usage("0\t/home/empty\n"), Some(0));
}

/// A product that does not fit 64 bits is not measured, never wrapped.
#[test]
fn an_overflowing_figure_is_not_measured() {
    assert_eq!(parse_home_usage("18446744073709551615\t/home/x\n"), None);
}

/// Anything that is not a plain figure first is not measured.
#[test]
fn malformed_answers_are_not_measured() {
    assert_eq!(parse_home_usage(""), None);
    assert_eq!(parse_home_usage("\n\n"), None);
    assert_eq!(parse_home_usage("du: cannot read\n"), None);
    assert_eq!(parse_home_usage("-5\t/home/x\n"), None);
    assert_eq!(parse_home_usage("12abc\t/home/x\n"), None);
    assert_eq!(parse_home_usage("1\0\t/home/x\n"), None);
}

/// Blank lines before the figure are skipped.
#[test]
fn blank_lines_before_the_figure_are_skipped() {
    assert_eq!(parse_home_usage("\n  \n4\t/home/x\n"), Some(4096));
}
