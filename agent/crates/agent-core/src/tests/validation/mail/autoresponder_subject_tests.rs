//! Tests for the `autoresponder_subject` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{AutoresponderSubject, MAXIMUM_LENGTH};
use crate::validation::mail::autoresponder_subject_error::AutoresponderSubjectError;

#[test]
fn a_plain_subject_in_any_script_is_accepted_unchanged() {
    for subject in [
        "Out of office",
        "\u{41d}\u{435} \u{432} \u{43e}\u{444}\u{438}\u{441}\u{435}",
        "Re: Your order #42 (urgent)",
    ] {
        assert_eq!(
            AutoresponderSubject::parse(subject).unwrap().as_str(),
            subject
        );
    }
}

#[test]
fn an_empty_subject_is_refused() {
    assert_eq!(
        AutoresponderSubject::parse(""),
        Err(AutoresponderSubjectError::Empty)
    );
}

#[test]
fn the_longest_subject_is_accepted_and_one_more_is_refused() {
    assert!(AutoresponderSubject::parse(&"a".repeat(MAXIMUM_LENGTH)).is_ok());
    assert_eq!(
        AutoresponderSubject::parse(&"a".repeat(MAXIMUM_LENGTH + 1)),
        Err(AutoresponderSubjectError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}

#[test]
fn the_two_characters_that_close_a_quoted_string_are_refused() {
    assert_eq!(
        AutoresponderSubject::parse("hi\"; discard; \""),
        Err(AutoresponderSubjectError::QuotingCharacter { character: '"' })
    );
    assert_eq!(
        AutoresponderSubject::parse("a\\b"),
        Err(AutoresponderSubjectError::QuotingCharacter { character: '\\' })
    );
}

#[test]
fn a_line_break_a_tab_and_a_text_override_are_refused() {
    for subject in [
        "a\nb",
        "a\rb",
        "a\tb",
        "a\u{0}b",
        "a\u{202E}b",
        "a\u{200B}b",
    ] {
        assert_eq!(
            AutoresponderSubject::parse(subject),
            Err(AutoresponderSubjectError::ControlCharacter),
            "{subject:?}"
        );
    }
}

#[test]
fn a_variable_expansion_is_refused() {
    assert_eq!(
        AutoresponderSubject::parse("Re: ${user}"),
        Err(AutoresponderSubjectError::VariableExpansion)
    );
}

#[test]
fn a_dollar_or_a_brace_alone_is_fine() {
    assert!(AutoresponderSubject::parse("Price: $5 {approx}").is_ok());
}

#[test]
fn the_line_and_paragraph_separators_and_the_tag_characters_are_refused() {
    for subject in ["a\u{2028}b", "a\u{2029}b", "a\u{E0041}b", "a\u{61C}b"] {
        assert_eq!(
            AutoresponderSubject::parse(subject),
            Err(AutoresponderSubjectError::ControlCharacter),
            "{subject:?}"
        );
    }
}
