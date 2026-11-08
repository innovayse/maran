//! Tests for reading the archive key off a backup destination.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_archive_key;
use crate::proto::{BackupDestination, BackupDestinationKind, ErrorCode};

/// A local destination carrying `key` and nothing else.
fn destination(key: &str) -> BackupDestination {
    BackupDestination {
        kind: BackupDestinationKind::Local as i32,
        archive_key: key.to_owned(),
        ..Default::default()
    }
}

#[test]
fn a_destination_with_no_key_asks_for_a_plain_archive() {
    assert!(
        validated_archive_key(Some(&destination("")))
            .unwrap()
            .is_none()
    );
}

#[test]
fn a_missing_destination_has_no_key_and_is_left_for_the_destination_check_to_refuse() {
    assert!(validated_archive_key(None).unwrap().is_none());
}

#[test]
fn a_whole_key_is_parsed() {
    let key = validated_archive_key(Some(&destination(&"0f".repeat(32))))
        .unwrap()
        .expect("a key was sent");

    assert_eq!(key.expose()[0], 0x0f);
}

#[test]
fn a_key_that_is_nearly_right_is_refused_and_not_read_as_no_key() {
    // An absent key on creation would write a PLAIN archive where a sealed one
    // was asked for, so the near miss must be an error, never `None`.
    let short = "0f".repeat(31);
    let error = validated_archive_key(Some(&destination(&short))).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(!error.message.contains(&short), "{}", error.message);
}

#[test]
fn an_uppercase_key_is_refused() {
    let error = validated_archive_key(Some(&destination(&"0F".repeat(32)))).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
