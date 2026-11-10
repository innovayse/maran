//! Property tests for [`Domain`]: whatever the generator hands it, an accepted name is
//! a well-formed lowercase hostname, a reserved one is never claimable, and a character
//! that could end the `server_name` line is refused wherever it is planted.
//!
//! Declared beside `domain_tests.rs` by `domain.rs`. Every property runs from the
//! fixed seed of `property_runs`, so a failure reproduces.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::prelude::*;
use proptest::sample::select;

use super::{Domain, DomainError, MAX_LABEL_LENGTH, MAX_LENGTH};
use crate::validation::property_draws::property_draws;
use crate::validation::property_insertion::inserted_at;
use crate::validation::property_runs::property_runs;

/// Characters a label is mostly made of.
const LABEL_CHARACTERS: [char; 6] = ['a', 'b', 'z', '0', '9', 'Q'];

/// The edge cases mixed in: the dot and the hyphen that shape labels, and the characters a
/// hostname has no place for (space, newline, NUL, `_`, `/`, `:`, one multi-byte letter).
const EDGE_CHARACTERS: [char; 9] = ['.', '-', '_', ' ', '\n', '\0', '/', ':', 'é'];

/// Top-level labels that no reservation touches.
const OPEN_SUFFIXES: [&str; 5] = ["com", "net", "org", "io", "shop"];

/// A character that is not a letter, digit, `-` or `.`: nothing a hostname may hold.
const ILLEGAL_CHARACTERS: [char; 14] = [
    ' ', '\n', '\r', '\0', '/', '\\', ';', '{', '}', '"', '\'', '_', 'é', '$',
];

/// Text mostly made of label characters with dots, hyphens and illegal characters mixed in.
fn dense_text() -> impl Strategy<Value = String> {
    let character = prop_oneof![
        8 => select(LABEL_CHARACTERS.to_vec()),
        3 => select(EDGE_CHARACTERS.to_vec()),
    ];

    proptest::collection::vec(character, 0..30)
        .prop_map(|characters| characters.into_iter().collect::<String>())
}

/// A name every rule accepts: one to three ordinary labels and an open suffix.
fn open_domain() -> impl Strategy<Value = String> {
    (
        proptest::collection::vec("[a-z][a-z0-9-]{0,10}[a-z0-9]", 1..=3),
        select(OPEN_SUFFIXES.to_vec()),
    )
        .prop_map(|(labels, suffix)| format!("{}.{suffix}", labels.join(".")))
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn an_accepted_existing_name_is_a_well_formed_lowercase_hostname(candidate in dense_text()) {
        if let Ok(domain) = Domain::parse_existing(&candidate) {
            let text = domain.as_str();
            prop_assert_eq!(text, candidate.to_ascii_lowercase());
            prop_assert!(text.len() <= MAX_LENGTH);
            prop_assert!(
                text.chars()
                    .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-' || c == '.')
            );
            for label in text.split('.') {
                prop_assert!(!label.is_empty());
                prop_assert!(label.len() <= MAX_LABEL_LENGTH);
                prop_assert!(!label.starts_with('-') && !label.ends_with('-'));
            }
        }
    }

    #[test]
    fn a_claimable_name_is_never_reserved(candidate in dense_text()) {
        if let Ok(domain) = Domain::parse(&candidate) {
            let text = domain.as_str();
            let final_label = text.rsplit('.').next().unwrap();

            prop_assert_eq!(Domain::parse_existing(&candidate), Ok(domain.clone()));
            prop_assert!(!final_label.bytes().all(|b| b.is_ascii_digit()));
            prop_assert!(text != "localhost" && !text.ends_with(".localhost"));
            for suffix in ["local", "localdomain", "internal", "lan", "home.arpa", "invalid"] {
                let dotted = format!(".{suffix}");

                prop_assert!(text != suffix && !text.ends_with(&dotted));
            }
        }
    }

    #[test]
    fn a_name_built_of_ordinary_labels_is_accepted_and_lowercased(
        candidate in open_domain(),
        shout in any::<bool>(),
    ) {
        let spelled = if shout { candidate.to_ascii_uppercase() } else { candidate.clone() };

        let parsed = Domain::parse(&spelled).unwrap();

        prop_assert_eq!(parsed.as_str(), candidate.as_str());
    }

    #[test]
    fn an_illegal_character_is_refused_wherever_it_is_planted(
        candidate in open_domain(),
        position in 0usize..80,
        planted in select(ILLEGAL_CHARACTERS.to_vec()),
    ) {
        let spelled = inserted_at(&candidate, position, planted);

        prop_assert_eq!(
            Domain::parse_existing(&spelled),
            Err(DomainError::IllegalCharacter { character: planted })
        );
        prop_assert_eq!(
            Domain::parse(&spelled),
            Err(DomainError::IllegalCharacter { character: planted })
        );
    }

    #[test]
    fn a_label_is_accepted_up_to_sixty_three_characters_and_not_beyond(length in 1usize..100) {
        let candidate = format!("{}.com", "a".repeat(length));

        prop_assert_eq!(Domain::parse(&candidate).is_ok(), length <= MAX_LABEL_LENGTH);
    }

    #[test]
    fn a_name_is_accepted_up_to_two_hundred_fifty_three_characters_and_not_beyond(
        lengths in proptest::collection::vec(1usize..=MAX_LABEL_LENGTH, 1..9),
    ) {
        let candidate = lengths
            .iter()
            .map(|length| "a".repeat(*length))
            .collect::<Vec<_>>()
            .join(".");
        let result = Domain::parse_existing(&candidate);

        prop_assert_eq!(result.is_ok(), candidate.len() <= MAX_LENGTH);
        if candidate.len() > MAX_LENGTH {
            prop_assert_eq!(result, Err(DomainError::TooLong));
        }
    }
}

/// The dense generator reaches both verdicts, so the first two properties compare something.
#[test]
fn the_dense_generator_produces_accepted_and_refused_names() {
    let drawn = property_draws(&dense_text(), 512);

    assert!(
        drawn
            .iter()
            .any(|text| Domain::parse_existing(text).is_ok())
    );
    assert!(
        drawn
            .iter()
            .any(|text| Domain::parse_existing(text).is_err())
    );
    assert!(drawn.iter().any(|text| Domain::parse(text).is_ok()));
}
