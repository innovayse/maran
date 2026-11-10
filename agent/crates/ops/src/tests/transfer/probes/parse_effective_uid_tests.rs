//! Tests for [`parse_effective_uid`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_effective_uid;

#[test]
fn one_decimal_number_is_the_uid() {
    assert_eq!(parse_effective_uid("0\n"), Some(0));
    assert_eq!(parse_effective_uid(" 1000 \n"), Some(1000));
}

#[test]
fn anything_else_is_not_a_uid() {
    for text in [
        "",
        "\n",
        "root",
        "-1",
        "+5",
        "1000 1001",
        "1000\n1001",
        "4294967296",
        "0x10",
        "١٢",
    ] {
        assert_eq!(parse_effective_uid(text), None, "{text:?}");
    }
}
