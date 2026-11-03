//! Tests for [`is_printable_token`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::is_printable_token;

#[test]
fn a_package_version_with_an_epoch_and_a_tilde_is_a_token() {
    assert!(is_printable_token("1:14.2.0-4ubuntu2~24.04.1"));
    assert!(is_printable_token("libstdc++6"));
}

#[test]
fn an_empty_string_is_not_a_token() {
    assert!(!is_printable_token(""));
}

#[test]
fn a_space_a_control_character_or_a_non_ascii_byte_is_not_a_token() {
    assert!(!is_printable_token("two words"));
    assert!(!is_printable_token("line\nbreak"));
    assert!(!is_printable_token("tab\there"));
    assert!(!is_printable_token("caf\u{e9}"));
}
