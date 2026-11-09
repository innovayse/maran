//! Tests for `parse_zone_serial`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_zone_serial;

#[test]
fn the_serial_line_of_a_status_answer_is_read() {
    assert_eq!(
        parse_zone_serial("name: example.com\ntype: primary\nserial: 2026110701\nnodes: 4\n"),
        Some(2_026_110_701)
    );
}

#[test]
fn an_answer_without_a_serial_or_with_a_bad_one_is_none() {
    assert_eq!(parse_zone_serial(""), None);
    assert_eq!(parse_zone_serial("name: example.com\n"), None);
    assert_eq!(parse_zone_serial("serial: abc\n"), None);
    assert_eq!(parse_zone_serial("serial: 99999999999\n"), None);
}
