//! Tests for the `dkim_selector` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{DkimSelector, MAXIMUM_LENGTH};
use crate::validation::mail::dkim_selector_error::DkimSelectorError;

#[test]
fn ordinary_selectors_are_accepted() {
    for selector in ["maran", "s2026-11", "k1", "a"] {
        assert_eq!(DkimSelector::parse(selector).unwrap().as_str(), selector);
    }
}

#[test]
fn an_empty_selector_is_refused() {
    assert_eq!(DkimSelector::parse(""), Err(DkimSelectorError::Empty));
}

#[test]
fn the_longest_selector_is_accepted_and_one_more_is_refused() {
    assert!(DkimSelector::parse(&"a".repeat(MAXIMUM_LENGTH)).is_ok());
    assert_eq!(
        DkimSelector::parse(&"a".repeat(MAXIMUM_LENGTH + 1)),
        Err(DkimSelectorError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}

#[test]
fn a_dot_a_slash_a_space_and_upper_case_are_refused_not_renamed() {
    for (candidate, character) in [
        ("a.b", '.'),
        ("a/b", '/'),
        ("a b", ' '),
        ("Ab", 'A'),
        ("a\nb", '\n'),
    ] {
        assert_eq!(
            DkimSelector::parse(candidate),
            Err(DkimSelectorError::IllegalCharacter { character }),
            "{candidate:?}"
        );
    }
}

#[test]
fn a_leading_or_trailing_hyphen_is_refused() {
    assert_eq!(
        DkimSelector::parse("-a"),
        Err(DkimSelectorError::EdgeNotAlphanumeric)
    );
    assert_eq!(
        DkimSelector::parse("a-"),
        Err(DkimSelectorError::EdgeNotAlphanumeric)
    );
}
