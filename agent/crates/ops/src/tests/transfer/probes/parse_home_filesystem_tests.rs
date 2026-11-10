//! Tests for [`parse_home_filesystem`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_home_filesystem;
use crate::transfer::model::source_disk::SourceDisk;

#[test]
fn the_three_figures_become_bytes_by_the_fragment_size() {
    assert_eq!(
        parse_home_filesystem("4096 1000 400\n"),
        Some(SourceDisk {
            free_bytes: 400 * 4096,
            total_bytes: 1000 * 4096,
        })
    );
}

#[test]
fn a_full_filesystem_is_a_reading() {
    assert_eq!(
        parse_home_filesystem("4096 1000 0"),
        Some(SourceDisk {
            free_bytes: 0,
            total_bytes: 4_096_000,
        })
    );
}

#[test]
fn a_line_that_is_not_three_decimal_numbers_is_not_a_reading() {
    for text in [
        "",
        "4096 1000",
        "4096 1000 400 7",
        "a b c",
        "4096 -1 4",
        "4096 1000 400.5",
    ] {
        assert_eq!(parse_home_filesystem(text), None, "{text:?}");
    }
}

#[test]
fn an_impossible_figure_is_not_a_reading() {
    // Zero fragment, zero total, more free than total, and an overflow.
    for text in [
        "0 1000 400",
        "4096 0 0",
        "4096 10 11",
        "18446744073709551615 2 1",
    ] {
        assert_eq!(parse_home_filesystem(text), None, "{text:?}");
    }
}
