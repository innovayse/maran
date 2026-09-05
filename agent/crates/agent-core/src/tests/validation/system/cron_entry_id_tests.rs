//! Tests for the `cron_entry_id` module.
//!
//! This id is the only variable part of three paths under an account's home, so
//! the tests that matter are the ones that show the grammar leaves no alphabet
//! a path attack could be written in.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CronEntryId, CronEntryIdError, UUID_LENGTH};

#[test]
fn a_lowercase_hyphenated_uuid_parses_and_is_kept_verbatim() {
    for candidate in [
        "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
    ] {
        assert_eq!(CronEntryId::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn an_id_that_would_escape_the_cron_directory_is_refused() {
    // Every one of these is what the type exists for: `Path::join` with an
    // absolute string replaces the path it is joined to, and `..` climbs out of
    // the account's home. None of them survives the length and alphabet checks,
    // which is why the path helpers carry no traversal check of their own.
    for candidate in [
        "../../etc/passwd",
        "/etc/cron.d/evil",
        "",
        "..",
        ".",
        "../../../../../../etc/shadow",
        "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa/../../../etc/passwd",
    ] {
        assert!(
            CronEntryId::parse(candidate).is_err(),
            "`{candidate}` must not become a path segment"
        );
    }
}

#[test]
fn a_separator_hidden_at_a_legal_length_is_refused() {
    // Exactly 36 characters, so the length check passes and the alphabet is
    // what has to refuse it.
    let candidate = "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0/4aa";
    assert_eq!(candidate.len(), UUID_LENGTH);
    assert_eq!(
        CronEntryId::parse(candidate),
        Err(CronEntryIdError::IllegalCharacter { character: '/' })
    );

    let candidate = "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4a\n";
    assert_eq!(candidate.len(), UUID_LENGTH);
    assert_eq!(
        CronEntryId::parse(candidate),
        Err(CronEntryIdError::IllegalCharacter { character: '\n' })
    );
}

#[test]
fn a_candidate_of_the_wrong_length_is_refused() {
    assert_eq!(
        CronEntryId::parse(""),
        Err(CronEntryIdError::WrongLength {
            expected: UUID_LENGTH,
            actual: 0,
        })
    );
    assert_eq!(
        CronEntryId::parse("3f2a1c049d5b4f0e8a3d71b2c6e0d4aa"),
        Err(CronEntryIdError::WrongLength {
            expected: UUID_LENGTH,
            actual: 32,
        })
    );
    assert_eq!(
        CronEntryId::parse("3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aaa"),
        Err(CronEntryIdError::WrongLength {
            expected: UUID_LENGTH,
            actual: 37,
        })
    );
}

#[test]
fn a_multi_byte_candidate_is_reported_in_the_unit_it_was_measured_in() {
    // 36 characters but 37 bytes. Reporting a character count beside a byte
    // comparison rendered this as "is exactly 36 bytes, not 36" — a refusal
    // whose own message says nothing is wrong.
    let candidate = "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4a\u{e9}";
    assert_eq!(candidate.chars().count(), UUID_LENGTH);
    assert_eq!(candidate.len(), UUID_LENGTH + 1);

    assert_eq!(
        CronEntryId::parse(candidate),
        Err(CronEntryIdError::WrongLength {
            expected: UUID_LENGTH,
            actual: UUID_LENGTH + 1,
        })
    );
}

#[test]
fn a_hyphen_outside_the_four_fixed_positions_is_refused() {
    assert_eq!(
        CronEntryId::parse("3f2a1c0-49d5b-4f0e-8a3d-71b2c6e0d4aa"),
        Err(CronEntryIdError::MisplacedHyphen { position: 7 })
    );
    assert_eq!(
        CronEntryId::parse("3f2a1c0409d5b-4f0e-8a3d-71b2c6e0d4aa"),
        Err(CronEntryIdError::MisplacedHyphen { position: 8 })
    );
}

#[test]
fn uppercase_hex_is_refused_so_one_entry_names_one_file() {
    assert_eq!(
        CronEntryId::parse("3F2A1C04-9d5b-4f0e-8a3d-71b2c6e0d4aa"),
        Err(CronEntryIdError::IllegalCharacter { character: 'F' })
    );
}

#[test]
fn a_non_hex_letter_is_refused() {
    assert_eq!(
        CronEntryId::parse("3f2a1c0g-9d5b-4f0e-8a3d-71b2c6e0d4aa"),
        Err(CronEntryIdError::IllegalCharacter { character: 'g' })
    );
}

#[test]
fn a_braced_or_prefixed_uuid_is_refused_because_an_id_has_one_spelling() {
    for candidate in [
        "{3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa}",
        "urn:uuid:3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa",
    ] {
        assert!(CronEntryId::parse(candidate).is_err());
    }
}
