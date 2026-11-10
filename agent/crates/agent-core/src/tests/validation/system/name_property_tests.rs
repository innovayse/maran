//! Property tests for [`AccountName`]: the rule is held for every input a
//! generator can reach, not for the handful an example thought of.
//!
//! Declared beside `name_tests.rs` by `name.rs`, and for the same reason: a child
//! module reaches the unit's private constants. Every property runs from the fixed
//! seed of `property_runs`, so a failure reproduces.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::prelude::*;
use proptest::sample::select;

use super::{AccountName, MAX_LENGTH, MIN_LENGTH, NameError};
use crate::validation::property_draws::property_draws;
use crate::validation::property_insertion::inserted_at;
use crate::validation::property_runs::property_runs;

/// Characters the rule accepts after the first position.
const ACCEPTED_CHARACTERS: [char; 6] = ['a', 'b', 'z', '0', '9', '_'];

/// Characters chosen to sit on every edge of the rule: the near misses (uppercase,
/// `-`, `.`, `/`), the separators and the ones a config file reads as structure
/// (space, newline, NUL), and one multi-byte letter.
const REFUSED_CHARACTERS: [char; 10] = ['A', 'Z', '-', '.', '/', ' ', '\n', '\0', 'é', ':'];

/// Text that is mostly accepted characters with a few edge cases mixed in, long
/// enough to cross both length bounds. Mostly accepted ones, so that a fair share of
/// the draws is a valid name and the property compares both verdicts.
fn dense_text() -> impl Strategy<Value = String> {
    let character = prop_oneof![
        8 => select(ACCEPTED_CHARACTERS.to_vec()),
        1 => select(REFUSED_CHARACTERS.to_vec()),
    ];

    proptest::collection::vec(character, 0..36)
        .prop_map(|characters| characters.into_iter().collect::<String>())
}

/// Whether `candidate` is a lowercase letter then lowercase letters, digits or `_`, 3 to 30 long.
fn is_documented_name(candidate: &str) -> bool {
    let mut characters = candidate.chars();
    (MIN_LENGTH..=MAX_LENGTH).contains(&candidate.len())
        && characters.next().is_some_and(|c| c.is_ascii_lowercase())
        && characters.all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_')
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn a_name_is_accepted_exactly_when_it_matches_the_documented_grammar(candidate in dense_text()) {
        match AccountName::parse(&candidate) {
            Ok(name) => {
                prop_assert!(is_documented_name(&candidate));
                prop_assert_eq!(name.as_str(), candidate.as_str());
            }
            Err(error) => {
                prop_assert!(!is_documented_name(&candidate));
                prop_assert_eq!(error, NameError::Invalid);
            }
        }
    }

    #[test]
    fn every_name_of_the_documented_shape_is_accepted_unchanged(
        candidate in "[a-z][a-z0-9_]{2,29}",
    ) {
        let name = AccountName::parse(&candidate).unwrap();

        prop_assert_eq!(name.as_str(), candidate.as_str());
    }

    #[test]
    fn a_character_outside_the_alphabet_is_refused_wherever_it_is_planted(
        name in "[a-z][a-z0-9_]{2,28}",
        position in 0usize..40,
        planted in any::<char>(),
    ) {
        prop_assume!(!(planted.is_ascii_lowercase() || planted.is_ascii_digit() || planted == '_'));

        let candidate = inserted_at(&name, position, planted);

        prop_assert_eq!(AccountName::parse(&candidate), Err(NameError::Invalid));
    }

    #[test]
    fn only_a_length_between_the_bounds_is_accepted(length in 0usize..100) {
        let candidate = "a".repeat(length);

        prop_assert_eq!(
            AccountName::parse(&candidate).is_ok(),
            (MIN_LENGTH..=MAX_LENGTH).contains(&length)
        );
    }
}

/// The generator above reaches both verdicts, so the first property compares something.
#[test]
fn the_dense_generator_produces_accepted_and_refused_names() {
    let drawn = property_draws(&dense_text(), 512);

    assert!(drawn.iter().any(|text| AccountName::parse(text).is_ok()));
    assert!(drawn.iter().any(|text| AccountName::parse(text).is_err()));
}
