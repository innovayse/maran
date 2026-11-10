//! Property tests for [`EnvVarValue`]: a value cron would silently alter on its way in is
//! never accepted, so the value the panel stores is the value cron applies.
//!
//! Declared beside `env_var_value_tests.rs` by `env_var_value.rs`. Every property runs from
//! the fixed seed of `property_runs`, so a failure reproduces.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::prelude::*;
use proptest::sample::select;

use super::{EnvVarValue, EnvVarValueError, MAX_LENGTH};
use crate::validation::property_draws::property_draws;
use crate::validation::property_insertion::inserted_at;
use crate::validation::property_runs::property_runs;

/// Characters a value is mostly made of: letters, digits and `=`, which cron leaves alone.
const ORDINARY_CHARACTERS: [char; 5] = ['a', 'b', '1', '=', 'é'];

/// The edge cases: both quotes, a blank, a newline, a tab and the `%` cron rewrites into a newline.
const EDGE_CHARACTERS: [char; 6] = ['"', '\'', ' ', '\n', '\t', '%'];

/// Control characters a crontab line cannot carry.
const CONTROL_CHARACTERS: [char; 5] = ['\0', '\n', '\r', '\t', '\u{7f}'];

/// Text mostly made of ordinary characters with quotes, blanks and the rewritten `%` mixed in, short
/// enough that a pair of quotes around it is common.
fn dense_text() -> impl Strategy<Value = String> {
    let character = prop_oneof![
        5 => select(ORDINARY_CHARACTERS.to_vec()),
        3 => select(EDGE_CHARACTERS.to_vec()),
    ];

    proptest::collection::vec(character, 0..8)
        .prop_map(|characters| characters.into_iter().collect::<String>())
}

/// The rule, restated independently of the implementation.
fn model_accepts(text: &str) -> bool {
    let characters: Vec<char> = text.chars().collect();
    let wrapped_in_quotes = characters.len() >= 2
        && (characters[0] == '"' || characters[0] == '\'')
        && characters[0] == characters[characters.len() - 1];

    text.len() <= MAX_LENGTH
        && !text.chars().any(char::is_control)
        && !text.contains('%')
        && !text.starts_with(char::is_whitespace)
        && !text.ends_with(char::is_whitespace)
        && !wrapped_in_quotes
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn a_value_is_accepted_exactly_when_the_model_says_so_and_is_kept_verbatim(
        text in dense_text(),
    ) {
        match EnvVarValue::parse(&text) {
            Ok(value) => {
                prop_assert!(model_accepts(&text));
                prop_assert_eq!(value.as_str(), text.as_str());
            }
            Err(_) => prop_assert!(!model_accepts(&text)),
        }
    }

    #[test]
    fn a_value_wrapped_in_matching_quotes_is_refused(
        inner in "[a-z =]{0,10}",
        quote in select(vec!['"', '\'']),
    ) {
        let text = format!("{quote}{inner}{quote}");

        prop_assert_eq!(
            EnvVarValue::parse(&text),
            Err(EnvVarValueError::Quoted { quote })
        );
    }

    #[test]
    fn a_quote_that_is_not_matched_is_an_ordinary_character(
        inner in "[a-z]{1,10}",
        quote in select(vec!['"', '\'']),
    ) {
        let other = if quote == '"' { '\'' } else { '"' };

        let opening_only = format!("{quote}{inner}");
        let closing_only = format!("{inner}{quote}");
        let mismatched = format!("{quote}{inner}{other}");

        prop_assert!(EnvVarValue::parse(&opening_only).is_ok());
        prop_assert!(EnvVarValue::parse(&closing_only).is_ok());
        prop_assert!(EnvVarValue::parse(&mismatched).is_ok());
        prop_assert!(EnvVarValue::parse(&quote.to_string()).is_ok());
    }

    #[test]
    fn a_percent_sign_is_refused_wherever_it_is_planted(
        inner in "[a-z]{1,10}",
        position in 0usize..12,
    ) {
        let text = inserted_at(&inner, position, '%');

        prop_assert_eq!(EnvVarValue::parse(&text), Err(EnvVarValueError::PercentSign));
    }

    #[test]
    fn a_control_character_is_refused_wherever_it_is_planted(
        inner in "[a-z]{1,10}",
        position in 0usize..12,
        planted in select(CONTROL_CHARACTERS.to_vec()),
    ) {
        let text = inserted_at(&inner, position, planted);

        prop_assert_eq!(
            EnvVarValue::parse(&text),
            Err(EnvVarValueError::ControlCharacter { character: planted })
        );
    }

    #[test]
    fn a_value_is_accepted_up_to_the_length_cron_keeps_and_not_beyond(
        length in 0usize..(MAX_LENGTH + 50),
    ) {
        let text = "a".repeat(length);

        prop_assert_eq!(EnvVarValue::parse(&text).is_ok(), length <= MAX_LENGTH);
    }
}

/// The dense generator reaches both verdicts, so the first property compares something.
#[test]
fn the_dense_generator_produces_accepted_and_refused_values() {
    let drawn = property_draws(&dense_text(), 512);

    assert!(drawn.iter().any(|text| EnvVarValue::parse(text).is_ok()));
    assert!(drawn.iter().any(|text| EnvVarValue::parse(text).is_err()));
}
