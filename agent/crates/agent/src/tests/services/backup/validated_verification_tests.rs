//! Tests for the checks a verification request passes before `ops` runs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_verification;
use crate::proto::{BackupDestination, BackupDestinationKind, ErrorCode, VerifyBackupRequest};

/// The id every request in this file names.
const BACKUP_ID: &str = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

/// A destination of `kind` carrying only what a local one needs.
fn destination(kind: BackupDestinationKind) -> BackupDestination {
    BackupDestination {
        kind: kind as i32,
        path: String::new(),
        s3_bucket: String::new(),
        s3_region: String::new(),
        s3_endpoint: String::new(),
        s3_access_key_id: String::new(),
        s3_secret_access_key: String::new(),
        s3_path_style: false,
    }
}

/// A request for `account` and `backup_id` against `destination`.
fn request(account: &str, backup_id: &str, destination: BackupDestination) -> VerifyBackupRequest {
    VerifyBackupRequest {
        account_username: account.to_owned(),
        backup_id: backup_id.to_owned(),
        destination: Some(destination),
    }
}

#[test]
fn a_verification_against_a_local_destination_is_accepted_with_its_account_and_id() {
    let (account, backup_id, _root) = validated_verification(&request(
        "alice",
        BACKUP_ID,
        destination(BackupDestinationKind::Local),
    ))
    .unwrap();

    assert_eq!(account.as_str(), "alice");
    assert_eq!(backup_id.as_str(), BACKUP_ID);
}

#[test]
fn a_verification_against_a_remote_destination_is_refused_as_not_implemented() {
    let mut s3 = destination(BackupDestinationKind::S3);
    s3.s3_bucket = "maran-backups".to_owned();
    s3.s3_region = "eu-central-1".to_owned();
    s3.s3_access_key_id = "AKIAEXAMPLE".to_owned();
    s3.s3_secret_access_key = "s3cr3t".to_owned();

    let error = validated_verification(&request("alice", BACKUP_ID, s3)).unwrap_err();

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused_as_invalid_input() {
    let error = validated_verification(&request(
        "../root",
        BACKUP_ID,
        destination(BackupDestinationKind::Local),
    ))
    .unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_backup_id_carrying_a_separator_is_refused_before_any_path_is_formed() {
    for id in ["../etc/passwd", "a/b", "", "3f2504e0\n4f89"] {
        let error = validated_verification(&request(
            "alice",
            id,
            destination(BackupDestinationKind::Local),
        ))
        .unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "id {id:?}");
    }
}

#[test]
fn a_verification_with_no_destination_is_refused() {
    let mut missing = request(
        "alice",
        BACKUP_ID,
        destination(BackupDestinationKind::Local),
    );
    missing.destination = None;

    assert!(validated_verification(&missing).is_err());
}
