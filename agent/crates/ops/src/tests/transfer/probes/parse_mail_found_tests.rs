//! Tests for [`parse_mail_found`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_mail_found;

#[test]
fn yes_is_found_and_no_is_not() {
    assert!(parse_mail_found("yes\n"));
    assert!(!parse_mail_found("no\n"));
}

#[test]
fn anything_but_the_word_yes_is_not_found() {
    for text in ["", "YES", "yes please", "yesno", "true"] {
        assert!(!parse_mail_found(text), "{text:?}");
    }
}
