//! Tests for the `bcrypt_hash` module.
//!
//! The hash is a credential and the second field of a `name:hash` line: the
//! shape check is what keeps a weak scheme, a colon or a newline out of it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{BcryptHash, BcryptHashError};

/// A syntactically valid hash: `$2y$`, cost 12, then 53 characters of the
/// bcrypt alphabet. Not the hash of any password anybody uses.
const VALID: &str = "$2y$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXY./";

#[test]
fn the_fixture_has_the_length_of_a_bcrypt_hash() {
    // Guards the fixture itself: every test below changes one thing about a
    // value that must otherwise be accepted.
    assert_eq!(VALID.len(), 60);
    assert!(BcryptHash::parse(VALID).is_ok());
}

#[test]
fn the_three_bcrypt_scheme_markers_and_the_cost_range_are_accepted() {
    for marker in ["$2a$", "$2b$", "$2y$"] {
        for cost in ["10", "12", "14"] {
            let hash = format!("{marker}{cost}{}", &VALID[6..]);

            assert_eq!(BcryptHash::parse(&hash).unwrap().expose(), hash);
        }
    }
}

#[test]
fn the_hash_never_appears_in_debug_or_display_output() {
    let hash = BcryptHash::parse(VALID).unwrap();

    assert!(!format!("{hash:?}").contains("abcdefghij"));
    assert!(format!("{hash:?}").contains("redacted"));
}

#[test]
fn a_control_character_is_refused_first() {
    let with_newline = format!("{}\n{}", &VALID[..30], &VALID[31..]);

    assert_eq!(
        BcryptHash::parse(&with_newline),
        Err(BcryptHashError::ControlCharacter)
    );
    assert_eq!(
        BcryptHash::parse("$2y$12$x\nuser2:evil"),
        Err(BcryptHashError::ControlCharacter)
    );
}

#[test]
fn a_hash_of_another_length_is_refused() {
    assert_eq!(
        BcryptHash::parse(""),
        Err(BcryptHashError::WrongLength {
            actual: 0,
            expected: 60
        })
    );
    assert_eq!(
        BcryptHash::parse(&VALID[..59]),
        Err(BcryptHashError::WrongLength {
            actual: 59,
            expected: 60
        })
    );
    assert_eq!(
        BcryptHash::parse(&format!("{VALID}x")),
        Err(BcryptHashError::WrongLength {
            actual: 61,
            expected: 60
        })
    );
}

#[test]
fn the_weak_schemes_are_refused_by_name() {
    // Padded to the right length so the scheme, not the length, is what refuses.
    let pad = |prefix: &str| format!("{prefix}{}", "a".repeat(60 - prefix.len()));

    for prefix in ["$apr1$", "{SHA}", "$1$", "$5$", "$6$", "$2x$12$", "plain"] {
        assert_eq!(
            BcryptHash::parse(&pad(prefix)),
            Err(BcryptHashError::UnsupportedScheme),
            "{prefix}"
        );
    }
}

#[test]
fn a_cost_outside_ten_to_fourteen_is_refused() {
    for cost in ["09", "15", "04", "31", "ab", "1x"] {
        let hash = format!("$2y${cost}{}", &VALID[6..]);

        assert_eq!(
            BcryptHash::parse(&hash),
            Err(BcryptHashError::CostOutOfRange {
                minimum: 10,
                maximum: 14
            }),
            "{cost}"
        );
    }
}

#[test]
fn a_character_outside_the_bcrypt_alphabet_is_refused() {
    // `:` would add a field, `$` and `+` are outside the alphabet, and a
    // missing `$` after the cost breaks the shape.
    for position in [7_usize, 30, 59] {
        for bad in [':', '$', '+', ' ', '"'] {
            let mut hash = VALID.to_owned();
            hash.replace_range(position..=position, &bad.to_string());

            assert_eq!(
                BcryptHash::parse(&hash),
                Err(BcryptHashError::ForbiddenCharacter),
                "{bad:?} at {position}"
            );
        }
    }
    let no_dollar = format!("$2y$12x{}", &VALID[7..]);

    assert_eq!(
        BcryptHash::parse(&no_dollar),
        Err(BcryptHashError::ForbiddenCharacter)
    );
}
