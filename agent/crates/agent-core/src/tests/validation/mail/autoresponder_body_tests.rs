//! Tests for the `autoresponder_body` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{AutoresponderBody, MAXIMUM_LENGTH};
use crate::validation::mail::autoresponder_body_error::AutoresponderBodyError;

#[test]
fn a_multi_line_body_is_accepted_unchanged() {
    let body = "Hello,\n\nI am away until Monday.\nThanks.\n";

    assert_eq!(AutoresponderBody::parse(body).unwrap().as_str(), body);
}

#[test]
fn an_empty_body_is_refused() {
    assert_eq!(
        AutoresponderBody::parse(""),
        Err(AutoresponderBodyError::Empty)
    );
}

#[test]
fn the_longest_body_is_accepted_and_one_more_byte_is_refused() {
    assert!(AutoresponderBody::parse(&"a".repeat(MAXIMUM_LENGTH)).is_ok());
    assert_eq!(
        AutoresponderBody::parse(&"a".repeat(MAXIMUM_LENGTH + 1)),
        Err(AutoresponderBodyError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}

#[test]
fn a_line_that_is_a_lone_dot_would_end_the_literal_and_is_refused() {
    assert_eq!(
        AutoresponderBody::parse("away\n.\ndiscard;"),
        Err(AutoresponderBodyError::LeadingDot)
    );
}

#[test]
fn any_line_beginning_with_a_dot_is_refused_including_the_first() {
    assert_eq!(
        AutoresponderBody::parse(".hidden"),
        Err(AutoresponderBodyError::LeadingDot)
    );
    assert_eq!(
        AutoresponderBody::parse("a\n..stuffed"),
        Err(AutoresponderBodyError::LeadingDot)
    );
}

#[test]
fn a_dot_inside_a_line_is_fine() {
    assert!(AutoresponderBody::parse("See you at 9.30 sharp.\nBye.").is_ok());
}

#[test]
fn a_carriage_return_a_tab_and_other_control_characters_are_refused() {
    for body in ["a\r\nb", "a\tb", "a\u{0}b", "a\u{1b}b", "a\u{202E}b"] {
        assert_eq!(
            AutoresponderBody::parse(body),
            Err(AutoresponderBodyError::ControlCharacter),
            "{body:?}"
        );
    }
}

#[test]
fn a_variable_expansion_is_refused() {
    assert_eq!(
        AutoresponderBody::parse("hi ${user}"),
        Err(AutoresponderBodyError::VariableExpansion)
    );
}

#[test]
fn a_dollar_or_a_brace_alone_is_fine() {
    assert!(AutoresponderBody::parse("Price: $5 {approx}").is_ok());
}

#[test]
fn the_line_and_paragraph_separators_and_the_tag_characters_are_refused() {
    for body in [
        "a\u{2028}b",
        "a\u{2029}.\nb",
        "a\u{E0041}b",
        "a\u{61C}b",
        "a\u{85}b",
    ] {
        assert_eq!(
            AutoresponderBody::parse(body),
            Err(AutoresponderBodyError::ControlCharacter),
            "{body:?}"
        );
    }
}
