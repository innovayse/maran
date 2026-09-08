//! Tests for the destination check that decides where a backup goes.
//!
//! The proposition this file exists for: an S3 destination is REFUSED, by
//! name, and is never quietly served from the local directory. That is the one
//! failure mode the whole seam investigation was about — `ops::backup` takes a
//! local root and nothing else, so a service that mapped every destination onto
//! it would report a successful local backup to an operator who configured a
//! bucket.
//!
//! Both halves are pinned, because a gate that has only ever been handed input
//! it must refuse passes just as well when it has been mutated to refuse
//! everything (rules/testing.md): the local destination must be ACCEPTED, and
//! it must be accepted as the agent's own root.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;

use super::validated_destination;
use crate::proto::{BackupDestination, BackupDestinationKind, ErrorCode};

/// A local destination as the panel sends one: a kind and nothing else.
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

/// A well formed S3 destination — nothing about it is malformed, so what
/// refuses it can only be the missing code path.
fn s3() -> BackupDestination {
    BackupDestination {
        kind: BackupDestinationKind::S3 as i32,
        path: "backups".to_owned(),
        s3_bucket: "maran-backups".to_owned(),
        s3_region: "eu-central-1".to_owned(),
        s3_endpoint: "https://s3.example.net".to_owned(),
        s3_access_key_id: "AKIAEXAMPLE".to_owned(),
        s3_secret_access_key: "s3cr3t".to_owned(),
        s3_path_style: true,
    }
}

#[test]
fn a_local_destination_is_accepted_as_the_agents_own_backup_root() {
    let root = validated_destination(Some(&local())).expect("a local destination is accepted");

    assert_eq!(root, LocalBackupRoot::default());
}

#[test]
fn a_well_formed_s3_destination_is_refused_as_not_implemented() {
    let error = validated_destination(Some(&s3())).expect_err("an S3 destination is refused");

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
    assert!(
        error.message.contains("local destination"),
        "the refusal says what this agent can do: {}",
        error.message
    );
}

#[test]
fn an_s3_destination_carries_neither_of_its_credentials_into_the_refusal() {
    let destination = s3();
    let error = validated_destination(Some(&destination)).expect_err("refused");

    // The credentials are secrets (rules/security.md item 8). The refusal is
    // operator-facing text that reaches a log, so it must not quote them.
    assert!(!error.message.contains(&destination.s3_access_key_id));
    assert!(!error.message.contains(&destination.s3_secret_access_key));
    assert!(
        !error
            .tool_output
            .contains(&destination.s3_secret_access_key)
    );
}

#[test]
fn a_malformed_bucket_is_reported_as_invalid_input_and_not_as_not_implemented() {
    let mut destination = s3();
    destination.s3_bucket = "Not A Bucket".to_owned();

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_s3_endpoint_that_is_not_https_is_refused_before_the_arm_is() {
    let mut destination = s3();
    destination.s3_endpoint = "http://s3.example.net".to_owned();

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.contains("https"));
}

#[test]
fn an_s3_destination_missing_a_credential_is_refused_as_invalid_input() {
    let mut destination = s3();
    destination.s3_secret_access_key = String::new();

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_local_destination_carrying_a_path_is_refused_rather_than_ignored() {
    let mut destination = local();
    destination.path = "nightly".to_owned();

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_local_destination_carrying_s3_fields_is_refused() {
    for mutate in [
        (|d: &mut BackupDestination| d.s3_bucket = "b".to_owned()) as fn(&mut BackupDestination),
        |d: &mut BackupDestination| d.s3_region = "eu-central-1".to_owned(),
        |d: &mut BackupDestination| d.s3_endpoint = "https://s3.example.net".to_owned(),
        |d: &mut BackupDestination| d.s3_access_key_id = "AKIA".to_owned(),
        |d: &mut BackupDestination| d.s3_secret_access_key = "s".to_owned(),
        |d: &mut BackupDestination| d.s3_path_style = true,
    ] {
        let mut destination = local();
        mutate(&mut destination);

        let error = validated_destination(Some(&destination)).expect_err("refused");
        assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_destination_naming_no_kind_is_refused() {
    let mut destination = local();
    destination.kind = BackupDestinationKind::Unspecified as i32;

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_destination_naming_a_kind_this_agent_does_not_know_is_refused() {
    let mut destination = local();
    destination.kind = 99;

    let error = validated_destination(Some(&destination)).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_request_with_no_destination_message_is_refused() {
    let error = validated_destination(None).expect_err("refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
