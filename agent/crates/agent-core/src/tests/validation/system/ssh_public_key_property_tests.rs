//! Property tests for [`SshPublicKey`]: whatever text arrives, an accepted key is a
//! key and nothing else, and what is written to `authorized_keys` carries no option, no
//! comment and no second line.
//!
//! Keys are built as the SSH wire format (RFC 4251 section 5) around random bytes, so the
//! properties exercise the structural checks and not only the text ones. Declared beside
//! `ssh_public_key_tests.rs` by `ssh_public_key.rs`. Every property runs from the fixed
//! seed of `property_runs`, so a failure reproduces.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;
use proptest::prelude::*;
use proptest::sample::select;

use super::{SshPublicKey, SshPublicKeyError};
use crate::validation::property_draws::property_draws;
use crate::validation::property_insertion::inserted_at;
use crate::validation::property_runs::property_runs;

/// Control characters a key line cannot carry.
const CONTROL_CHARACTERS: [char; 6] = ['\0', '\n', '\r', '\t', '\u{7f}', '\u{85}'];

/// The characters of the base64 alphabet, plus the padding sign.
fn base64_characters() -> Vec<char> {
    ('A'..='Z')
        .chain('a'..='z')
        .chain('0'..='9')
        .chain(['+', '/', '='])
        .collect()
}

/// One SSH wire-format `string`: a big-endian `u32` length and the bytes.
fn wire_string(bytes: &[u8]) -> Vec<u8> {
    let mut encoded = u32::try_from(bytes.len()).unwrap().to_be_bytes().to_vec();
    encoded.extend_from_slice(bytes);

    encoded
}

/// The wire blob of an Ed25519 key holding `key`, which is 32 bytes when well formed.
fn ed25519_blob(key: &[u8]) -> Vec<u8> {
    let mut blob = wire_string(b"ssh-ed25519");
    blob.extend(wire_string(key));

    blob
}

/// The `authorized_keys` text of a blob: `ssh-ed25519 <base64>`.
fn ed25519_line(blob: &[u8]) -> String {
    format!("ssh-ed25519 {}", STANDARD.encode(blob))
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn any_ed25519_key_is_accepted_and_its_comment_is_dropped(
        key in proptest::array::uniform32(any::<u8>()),
        comment in "[ -~]{0,30}",
    ) {
        let line = ed25519_line(&ed25519_blob(&key));
        let parsed = SshPublicKey::parse(&format!("{line} {comment}")).unwrap();

        prop_assert_eq!(parsed.algorithm(), "ssh-ed25519");
        prop_assert_eq!(parsed.as_line(), line);
    }

    #[test]
    fn bytes_behind_a_valid_key_are_refused(
        key in proptest::array::uniform32(any::<u8>()),
        trailing in proptest::collection::vec(any::<u8>(), 1..16),
    ) {
        let mut blob = ed25519_blob(&key);
        blob.extend(trailing);

        prop_assert_eq!(
            SshPublicKey::parse(&ed25519_line(&blob)),
            Err(SshPublicKeyError::Malformed)
        );
    }

    #[test]
    fn an_ed25519_key_of_any_other_length_is_refused(
        key in proptest::collection::vec(any::<u8>(), 0..64),
    ) {
        prop_assume!(key.len() != 32);

        prop_assert_eq!(
            SshPublicKey::parse(&ed25519_line(&ed25519_blob(&key))),
            Err(SshPublicKeyError::Malformed)
        );
    }

    #[test]
    fn a_key_cut_short_is_refused(
        key in proptest::array::uniform32(any::<u8>()),
        cut in 0usize..51,
    ) {
        let blob = ed25519_blob(&key);

        prop_assert!(SshPublicKey::parse(&ed25519_line(&blob[..cut])).is_err());
    }

    #[test]
    fn a_control_character_is_refused_wherever_it_is_planted(
        key in proptest::array::uniform32(any::<u8>()),
        position in 0usize..100,
        planted in select(CONTROL_CHARACTERS.to_vec()),
    ) {
        let spelled = inserted_at(&ed25519_line(&ed25519_blob(&key)), position, planted);

        prop_assert_eq!(SshPublicKey::parse(&spelled), Err(SshPublicKeyError::ControlCharacter));
    }

    #[test]
    fn an_option_in_front_of_a_key_is_never_accepted(
        option in "[a-z=\",/][a-z=\",/ ]{0,19}",
        key in proptest::array::uniform32(any::<u8>()),
    ) {
        let spelled = format!("{option} {}", ed25519_line(&ed25519_blob(&key)));

        prop_assert_eq!(
            SshPublicKey::parse(&spelled),
            Err(SshPublicKeyError::UnsupportedAlgorithm)
        );
    }

    #[test]
    fn whatever_is_accepted_is_written_as_one_line_that_parses_back_to_itself(
        key in proptest::array::uniform32(any::<u8>()),
        position in 12usize..60,
        replacement in select(base64_characters()),
    ) {
        let mut characters: Vec<char> = ed25519_line(&ed25519_blob(&key)).chars().collect();
        let index = position.min(characters.len() - 1);
        characters[index] = replacement;
        let mutated: String = characters.into_iter().collect();

        if let Ok(parsed) = SshPublicKey::parse(&mutated) {
            let line = parsed.as_line();

            prop_assert!(!line.chars().any(char::is_control));
            prop_assert_eq!(line.split(' ').count(), 2);
            prop_assert_eq!(SshPublicKey::parse(&line), Ok(parsed));
        }
    }

    #[test]
    fn arbitrary_text_never_makes_the_parser_panic_and_never_yields_a_line_with_a_blank_field(
        text in any::<String>(),
    ) {
        if let Ok(parsed) = SshPublicKey::parse(&text) {
            prop_assert!(parsed.as_line().split(' ').all(|field| !field.is_empty()));
        }
    }
}

/// The mutation generator reaches both verdicts, so the round-trip property compares something.
#[test]
fn mutated_keys_are_sometimes_accepted_and_sometimes_refused() {
    let strategy = (
        proptest::array::uniform32(any::<u8>()),
        12usize..60,
        select(base64_characters()),
    )
        .prop_map(|(key, position, replacement)| {
            let mut characters: Vec<char> = ed25519_line(&ed25519_blob(&key)).chars().collect();
            let index = position.min(characters.len() - 1);
            characters[index] = replacement;
            characters.into_iter().collect::<String>()
        });
    let drawn = property_draws(&strategy, 512);

    assert!(drawn.iter().any(|text| SshPublicKey::parse(text).is_ok()));
    assert!(drawn.iter().any(|text| SshPublicKey::parse(text).is_err()));
}
