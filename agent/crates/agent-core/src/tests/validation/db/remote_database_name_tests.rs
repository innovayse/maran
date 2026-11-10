//! Tests for the `remote_database_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RemoteDatabaseName;
use crate::validation::db::remote_database_name_error::RemoteDatabaseNameError;

#[test]
fn letters_digits_and_underscores_parse() {
    assert_eq!(
        RemoteDatabaseName::parse("Shop_2024").unwrap().as_str(),
        "Shop_2024"
    );
}

#[test]
fn the_longest_accepted_name_is_sixty_four_characters() {
    assert!(RemoteDatabaseName::parse(&"a".repeat(64)).is_ok());
    assert_eq!(
        RemoteDatabaseName::parse(&"a".repeat(65)),
        Err(RemoteDatabaseNameError::TooLong { maximum: 64 })
    );
}

#[test]
fn an_empty_name_is_refused() {
    assert_eq!(
        RemoteDatabaseName::parse(""),
        Err(RemoteDatabaseNameError::Empty)
    );
}

#[test]
fn every_character_a_shell_or_a_quote_needs_is_refused() {
    for candidate in [
        "a b", "a'b", "a;b", "a`b", "a$b", "a\nb", "a-b", "a.b", "a/b", "é",
    ] {
        assert_eq!(
            RemoteDatabaseName::parse(candidate),
            Err(RemoteDatabaseNameError::UnexpectedCharacter),
            "{candidate:?} must be refused"
        );
    }
}
