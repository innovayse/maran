//! Tests for the `backup_id` module.
//!
//! This id is the only variable part of an artifact's path under
//! `/var/backups/maran` and of its object key in a bucket, so the tests that
//! matter are the ones showing the grammar leaves no alphabet a path or key
//! attack could be written in.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{BackupId, BackupIdError, UUID_LENGTH};

#[test]
fn a_plain_uuid_parses() {
    for candidate in [
        "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
    ] {
        assert_eq!(BackupId::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn an_uppercase_uuid_is_refused() {
    // One backup is one file and one object key. A filesystem that
    // distinguishes case would give `3F2A…` and `3f2a…` two artifacts for what
    // the panel believes is one backup; S3 distinguishes case always.
    assert_eq!(
        BackupId::parse("3F2A1C04-9d5b-4f0e-8a3d-71b2c6e0d4aa"),
        Err(BackupIdError::IllegalCharacter { character: 'F' })
    );
    assert!(BackupId::parse("3F2A1C04-9D5B-4F0E-8A3D-71B2C6E0D4AA").is_err());
}

#[test]
fn a_path_separator_is_refused() {
    // Exactly 36 characters, so the length check passes and the alphabet is
    // what has to refuse it.
    let candidate = "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0/4aa";
    assert_eq!(candidate.len(), UUID_LENGTH);
    assert_eq!(
        BackupId::parse(candidate),
        Err(BackupIdError::IllegalCharacter { character: '/' })
    );
}

#[test]
fn a_traversal_segment_is_refused() {
    for candidate in [
        "..",
        ".",
        "../../etc/passwd",
        "/var/backups/maran/../../etc/shadow",
        "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa/../../../etc/passwd",
    ] {
        assert!(
            BackupId::parse(candidate).is_err(),
            "`{candidate}` must not become a path segment or an object key"
        );
    }
}

#[test]
fn the_empty_string_is_refused() {
    assert_eq!(
        BackupId::parse(""),
        Err(BackupIdError::WrongLength {
            expected: UUID_LENGTH,
            actual: 0,
        })
    );
}
