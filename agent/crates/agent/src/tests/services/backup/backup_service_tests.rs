//! Tests for the checks each backup rpc runs before it calls `ops`.
//!
//! The handlers themselves are three lines each — validate, call, map — and the
//! parts worth testing are the validators, which is why they are named
//! functions rather than code inlined in a handler: a decision inlined in a
//! handler can be deleted without a single test going red (rules/rust.md).
//!
//! What this file pins is that every rpc carrying a destination refuses a
//! remote one. Three rpcs, three separate checks in the code, so three separate
//! assertions here: a refusal proven on `CreateBackup` says nothing about
//! `DeleteBackup`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{validated_creation, validated_removal};
use crate::proto::{
    BackupDestination, BackupDestinationKind, CreateBackupRequest, DeleteBackupRequest, ErrorCode,
};

/// The id every request in this file names.
const BACKUP_ID: &str = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

/// A local destination: a kind and nothing else.
fn local() -> BackupDestination {
    BackupDestination {
        kind: BackupDestinationKind::Local as i32,
        path: String::new(),
        s3_bucket: String::new(),
        s3_region: String::new(),
        s3_endpoint: String::new(),
        s3_access_key_id: String::new(),
        s3_secret_access_key: String::new(),
        s3_path_style: false,
    }
}

/// A well formed S3 destination.
fn s3() -> BackupDestination {
    BackupDestination {
        kind: BackupDestinationKind::S3 as i32,
        path: String::new(),
        s3_bucket: "maran-backups".to_owned(),
        s3_region: "eu-central-1".to_owned(),
        s3_endpoint: String::new(),
        s3_access_key_id: "AKIAEXAMPLE".to_owned(),
        s3_secret_access_key: "s3cr3t".to_owned(),
        s3_path_style: false,
    }
}

/// A creation request against `destination`.
fn creation(destination: BackupDestination) -> CreateBackupRequest {
    CreateBackupRequest {
        account_username: "alice".to_owned(),
        backup_id: BACKUP_ID.to_owned(),
        destination: Some(destination),
    }
}

/// A deletion request against `destination`.
fn removal(destination: BackupDestination) -> DeleteBackupRequest {
    DeleteBackupRequest {
        account_username: "alice".to_owned(),
        backup_id: BACKUP_ID.to_owned(),
        destination: Some(destination),
    }
}

#[test]
fn a_creation_against_a_local_destination_is_accepted_with_its_account_and_id() {
    let (account, backup_id, _root) =
        validated_creation(&creation(local())).expect("a local creation is accepted");

    assert_eq!(account.as_str(), "alice");
    assert_eq!(backup_id.as_str(), BACKUP_ID);
}

#[test]
fn a_creation_against_a_remote_destination_is_refused_as_not_implemented() {
    let error = validated_creation(&creation(s3())).expect_err("refused");

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
}

#[test]
fn a_deletion_against_a_local_destination_is_accepted() {
    let (account, backup_id, _root) =
        validated_removal(&removal(local())).expect("a local deletion is accepted");

    assert_eq!(account.as_str(), "alice");
    assert_eq!(backup_id.as_str(), BACKUP_ID);
}

#[test]
fn a_deletion_against_a_remote_destination_is_refused_as_not_implemented() {
    let error = validated_removal(&removal(s3())).expect_err("refused");

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused_before_the_destination_is_read() {
    let mut request = creation(local());
    request.account_username = "../root".to_owned();

    let error = validated_creation(&request).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_backup_id_that_is_not_a_uuid_is_refused_on_both_rpcs() {
    let mut created = creation(local());
    created.backup_id = "/etc/cron.d/pwn".to_owned();
    assert_eq!(
        validated_creation(&created).expect_err("refused").code,
        ErrorCode::InvalidInput as i32
    );

    let mut removed = removal(local());
    removed.backup_id = "..".to_owned();
    assert_eq!(
        validated_removal(&removed).expect_err("refused").code,
        ErrorCode::InvalidInput as i32
    );
}
