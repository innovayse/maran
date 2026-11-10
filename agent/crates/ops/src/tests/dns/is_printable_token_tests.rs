//! The one test every word read out of the daemon's output passes first.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::is_printable_token;

#[test]
fn an_ordinary_word_is_a_token() {
    assert!(is_printable_token("maran-csk"));
    assert!(is_printable_token("12345/ECDSAP256SHA256"));
}

#[test]
fn the_empty_string_is_not_a_token() {
    assert!(!is_printable_token(""));
}

#[test]
fn a_space_a_control_character_or_a_non_ascii_character_is_not_a_token() {
    for text in ["a b", "a\tb", "a\u{1b}[31m", "a\0b", "a\nb", "caf\u{e9}"] {
        assert!(!is_printable_token(text), "{text:?} was accepted");
    }
}
