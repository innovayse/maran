//! Tests for the text a name search looks for.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SearchQuery;
use crate::validation::fs::search_query_error::SearchQueryError;

#[test]
fn a_query_matches_a_substring_of_a_name_ignoring_case() {
    let query = SearchQuery::parse("ReadMe").unwrap();

    assert!(query.matches("README.md"));
    assert!(query.matches("my-readme-final.txt"));
    assert!(!query.matches("notes.txt"));
}

#[test]
fn a_query_is_a_literal_and_not_a_pattern() {
    let query = SearchQuery::parse("*.php").unwrap();

    assert!(query.matches("a*.php"));
    assert!(!query.matches("index.php"));
}

#[test]
fn an_empty_query_is_refused() {
    assert_eq!(SearchQuery::parse("").unwrap_err(), SearchQueryError::Empty);
}

#[test]
fn a_query_over_255_bytes_is_refused_and_255_is_accepted() {
    assert_eq!(
        SearchQuery::parse(&"a".repeat(256)).unwrap_err(),
        SearchQueryError::TooLong
    );
    assert!(SearchQuery::parse(&"a".repeat(255)).is_ok());
}

#[test]
fn a_separator_is_refused() {
    assert_eq!(
        SearchQuery::parse("a/b").unwrap_err(),
        SearchQueryError::ContainsSeparator
    );
}

#[test]
fn a_control_character_is_refused() {
    for text in ["a\0", "a\n", "\r"] {
        assert_eq!(
            SearchQuery::parse(text).unwrap_err(),
            SearchQueryError::ForbiddenCharacter
        );
    }
}
