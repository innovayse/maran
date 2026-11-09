//! Tests for the `check_local_part` module.
//!
//! The local part is written into a whitespace-separated map, a colon-separated
//! login database and a directory name, so the cases are the separators of each.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, check_local_part};
use crate::validation::mail::mail_local_part_error::MailLocalPartError;

#[test]
fn an_ordinary_local_part_is_accepted_and_lowercased() {
    assert_eq!(
        check_local_part("Alice.Smith", false).unwrap(),
        "alice.smith"
    );
    assert_eq!(check_local_part("a_b-c.d9", false).unwrap(), "a_b-c.d9");
}

#[test]
fn the_longest_local_part_is_accepted_and_one_more_is_refused() {
    let longest = "a".repeat(MAXIMUM_LENGTH);

    assert!(check_local_part(&longest, false).is_ok());
    assert_eq!(
        check_local_part(&format!("{longest}a"), false),
        Err(MailLocalPartError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}

#[test]
fn an_empty_local_part_is_refused() {
    assert_eq!(check_local_part("", false), Err(MailLocalPartError::Empty));
}

#[test]
fn every_separator_a_config_file_uses_is_refused() {
    for separator in [
        ' ', ':', ',', ';', '"', '\'', '\\', '/', '|', '@', '\n', '\r', '\t', '\0', '%', '!', '#',
    ] {
        let candidate = format!("a{separator}b");

        assert_eq!(
            check_local_part(&candidate, true),
            Err(MailLocalPartError::IllegalCharacter {
                character: separator
            }),
            "{separator:?}"
        );
    }
}

#[test]
fn a_non_ascii_letter_is_refused() {
    assert_eq!(
        check_local_part("jos\u{e9}", false),
        Err(MailLocalPartError::IllegalCharacter {
            character: '\u{e9}'
        })
    );
}

#[test]
fn the_extension_delimiter_is_refused_unless_a_target_asks_for_it() {
    assert_eq!(
        check_local_part("name+tag", false),
        Err(MailLocalPartError::IllegalCharacter { character: '+' })
    );
    assert_eq!(check_local_part("name+tag", true).unwrap(), "name+tag");
}

#[test]
fn a_local_part_must_start_and_end_with_a_letter_or_digit() {
    for candidate in [".a", "a.", "-a", "a-", "_a", "a_", "a+"] {
        assert_eq!(
            check_local_part(candidate, true),
            Err(MailLocalPartError::EdgeNotAlphanumeric),
            "{candidate}"
        );
    }
}

#[test]
fn two_dots_in_a_row_are_refused() {
    assert_eq!(
        check_local_part("a..b", false),
        Err(MailLocalPartError::ConsecutiveDots)
    );
}

#[test]
fn an_illegal_character_is_named_before_the_shape_rules() {
    // The leading dot would also fail the edge rule; the newline is the
    // finding worth reporting.
    assert_eq!(
        check_local_part(".a\nb", false),
        Err(MailLocalPartError::IllegalCharacter { character: '\n' })
    );
}
