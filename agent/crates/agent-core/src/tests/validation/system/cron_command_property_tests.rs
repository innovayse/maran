//! Property tests for [`CronCommand`]: one line, no control character, no surrounding
//! whitespace, bounded, and every other byte left exactly as the customer wrote it.
//!
//! Declared beside `cron_command_tests.rs` by `cron_command.rs`. Every property runs from
//! the fixed seed of `property_runs`, so a failure reproduces.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::prelude::*;
use proptest::sample::select;

use super::{CronCommand, CronCommandError, MAX_LENGTH};
use crate::validation::property_draws::property_draws;
use crate::validation::property_insertion::inserted_at;
use crate::validation::property_runs::property_runs;

/// Characters a command is mostly made of, `%` and `#` included on purpose: they are ordinary
/// text in a file, and a regression that refused them would refuse working commands.
const ORDINARY_CHARACTERS: [char; 9] = ['a', 'b', '0', '%', '#', ';', '$', '/', 'é'];

/// The edge cases: every kind of blank, and the controls a line cannot carry (`\u{85}` and
/// `\u{7f}` are control characters that are NOT ASCII whitespace; `\u{a0}` is whitespace that is NOT control).
const EDGE_CHARACTERS: [char; 8] = [' ', '\t', '\n', '\r', '\0', '\u{85}', '\u{7f}', '\u{a0}'];

/// Every control character the type must refuse, however it is placed.
const CONTROL_CHARACTERS: [char; 7] = ['\0', '\n', '\r', '\t', '\u{1}', '\u{7f}', '\u{85}'];

/// Text mostly made of ordinary characters with blanks and controls mixed in.
fn dense_text() -> impl Strategy<Value = String> {
    let character = prop_oneof![
        8 => select(ORDINARY_CHARACTERS.to_vec()),
        2 => select(EDGE_CHARACTERS.to_vec()),
    ];

    proptest::collection::vec(character, 0..20)
        .prop_map(|characters| characters.into_iter().collect::<String>())
}

/// A command the rule accepts: ordinary characters, with single interior spaces.
fn ordinary_command() -> impl Strategy<Value = String> {
    "[a-z0-9%#;&|$./=-]( ?[a-z0-9%#;&|$./=-]){0,30}"
}

/// The rule, restated independently of the implementation.
fn model_accepts(text: &str) -> bool {
    (1..=MAX_LENGTH).contains(&text.len())
        && !text.chars().any(char::is_control)
        && !text.starts_with(char::is_whitespace)
        && !text.ends_with(char::is_whitespace)
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn a_command_is_accepted_exactly_when_the_model_says_so_and_is_kept_verbatim(
        text in dense_text(),
    ) {
        match CronCommand::parse(&text) {
            Ok(command) => {
                prop_assert!(model_accepts(&text));
                prop_assert_eq!(command.as_str(), text.as_str());
                prop_assert!(!command.as_str().contains(['\n', '\r', '\0']));
            }
            Err(_) => prop_assert!(!model_accepts(&text)),
        }
    }

    #[test]
    fn every_ordinary_command_is_accepted_percent_and_hash_included(text in ordinary_command()) {
        let parsed = CronCommand::parse(&text).unwrap();

        prop_assert_eq!(parsed.as_str(), text.as_str());
    }

    #[test]
    fn a_control_character_is_refused_wherever_it_is_planted(
        text in ordinary_command(),
        position in 0usize..40,
        planted in select(CONTROL_CHARACTERS.to_vec()),
    ) {
        let spelled = inserted_at(&text, position, planted);

        prop_assert_eq!(
            CronCommand::parse(&spelled),
            Err(CronCommandError::ControlCharacter { character: planted })
        );
    }

    #[test]
    fn a_blank_at_either_end_is_refused(text in ordinary_command(), leading in any::<bool>()) {
        let spelled = if leading { format!(" {text}") } else { format!("{text} ") };

        prop_assert_eq!(
            CronCommand::parse(&spelled),
            Err(CronCommandError::SurroundingWhitespace)
        );
    }

    #[test]
    fn a_command_is_accepted_up_to_four_thousand_ninety_six_bytes_and_not_beyond(
        length in 0usize..4200,
    ) {
        let text = "a".repeat(length);

        prop_assert_eq!(CronCommand::parse(&text).is_ok(), (1..=MAX_LENGTH).contains(&length));
    }
}

/// The dense generator reaches both verdicts, so the first property compares something.
#[test]
fn the_dense_generator_produces_accepted_and_refused_commands() {
    let drawn = property_draws(&dense_text(), 512);

    assert!(drawn.iter().any(|text| CronCommand::parse(text).is_ok()));
    assert!(drawn.iter().any(|text| CronCommand::parse(text).is_err()));
}
