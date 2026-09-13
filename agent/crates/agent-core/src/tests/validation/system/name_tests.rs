//! Tests for the `name` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `name.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{AccountName, MAX_LENGTH, NameError};

#[test]
fn shortest_allowed_name_is_accepted() {
    assert_eq!(AccountName::parse("abc").unwrap().as_str(), "abc");
}

#[test]
fn name_with_digits_and_underscore_is_accepted() {
    assert_eq!(
        AccountName::parse("client_42").unwrap().as_str(),
        "client_42"
    );
}

#[test]
fn longest_allowed_name_is_accepted() {
    let candidate = "a".repeat(MAX_LENGTH);
    assert_eq!(AccountName::parse(&candidate).unwrap().as_str(), candidate);
}

#[test]
fn empty_name_is_rejected() {
    assert_eq!(AccountName::parse(""), Err(NameError::Invalid));
}

#[test]
fn too_short_name_is_rejected() {
    assert_eq!(AccountName::parse("ab"), Err(NameError::Invalid));
}

#[test]
fn too_long_name_is_rejected() {
    let candidate = "a".repeat(MAX_LENGTH + 1);
    assert_eq!(AccountName::parse(&candidate), Err(NameError::Invalid));
}

#[test]
fn name_starting_with_a_digit_is_rejected() {
    assert_eq!(AccountName::parse("1abc"), Err(NameError::Invalid));
}

#[test]
fn name_with_uppercase_is_rejected() {
    assert_eq!(AccountName::parse("Abc"), Err(NameError::Invalid));
}

#[test]
fn name_with_hyphen_is_rejected() {
    assert_eq!(AccountName::parse("a-b"), Err(NameError::Invalid));
}

#[test]
fn name_with_space_is_rejected() {
    assert_eq!(AccountName::parse("a b"), Err(NameError::Invalid));
}

#[test]
fn name_carrying_a_shell_command_is_rejected() {
    assert_eq!(AccountName::parse("a;rm -rf /"), Err(NameError::Invalid));
}

#[test]
fn non_ascii_name_is_rejected() {
    assert_eq!(AccountName::parse("cafe\u{301}"), Err(NameError::Invalid));
}

#[test]
fn name_with_newline_is_rejected() {
    assert_eq!(AccountName::parse("a\nb"), Err(NameError::Invalid));
}
