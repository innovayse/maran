//! Tests for the restore request the agent re-checks before it drops anything.
//!
//! Two propositions carry this file. The digest is REQUIRED and its absence is
//! not read as "do not check" — that comparison is the only thing standing
//! between a restore and bytes somebody else chose. And a database name is
//! accepted only when it decodes to the account being restored, so a request
//! cannot name a neighbour's database and have it dropped on the way past.
//!
//! Each refusal has its acceptance beside it: a gate mutated to refuse
//! everything passes every test that only hands it broken input
//! (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::ValidatedRestore;
use crate::proto::{BackupDestination, BackupDestinationKind, ErrorCode, RestoreBackupRequest};

/// A digest of the right length and alphabet.
const DIGEST: &str = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

/// The account every request in this file is restored into.
fn account() -> AccountName {
    AccountName::parse("alice").expect("a valid account name")
}

/// A request the agent accepts in full.
fn request() -> RestoreBackupRequest {
    RestoreBackupRequest {
        account_username: "alice".to_owned(),
        backup_id: "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
        destination: Some(BackupDestination {
            kind: BackupDestinationKind::Local as i32,
            path: String::new(),
            s3_bucket: String::new(),
            s3_region: String::new(),
            s3_endpoint: String::new(),
            s3_access_key_id: String::new(),
            s3_secret_access_key: String::new(),
            s3_path_style: false,
        }),
        expected_sha256: DIGEST.to_owned(),
        allowed_databases: vec!["alice_shop".to_owned()],
    }
}

#[test]
fn a_well_formed_restore_request_is_accepted_with_every_field_carried_through() {
    let input = ValidatedRestore::from_request(&request(), &account())
        .unwrap_or_else(|error| panic!("accepted: {error:?}"));

    assert_eq!(
        input.backup_id.as_str(),
        "3f2504e0-4f89-41d3-9a0c-0305e82c3301"
    );
    assert_eq!(input.expected_sha256, DIGEST);
    assert_eq!(input.allowed_databases.len(), 1);
    assert_eq!(input.allowed_databases[0].as_str(), "alice_shop");
}

#[test]
fn a_restore_with_no_expected_checksum_is_refused() {
    let mut wire = request();
    wire.expected_sha256 = String::new();

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_checksum_of_the_wrong_length_is_refused() {
    let mut wire = request();
    wire.expected_sha256 = DIGEST[..63].to_owned();

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_uppercase_checksum_is_refused_rather_than_lowercased() {
    let mut wire = request();
    wire.expected_sha256 = DIGEST.to_ascii_uppercase();

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_checksum_holding_a_character_outside_hexadecimal_is_refused() {
    let mut wire = request();
    wire.expected_sha256 = format!("{}z", &DIGEST[..63]);

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_database_belonging_to_another_account_is_refused() {
    let mut wire = request();
    wire.allowed_databases = vec!["bob_shop".to_owned()];

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_allowed_database_list_is_accepted_and_stays_empty() {
    let mut wire = request();
    wire.allowed_databases = Vec::new();

    let input = ValidatedRestore::from_request(&wire, &account())
        .unwrap_or_else(|error| panic!("accepted: {error:?}"));

    assert!(input.allowed_databases.is_empty());
}

#[test]
fn a_backup_id_that_is_not_a_uuid_is_refused() {
    let mut wire = request();
    wire.backup_id = "../../etc/cron.d/pwn".to_owned();

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_restore_to_a_remote_destination_is_refused_as_not_implemented() {
    let mut wire = request();
    if let Some(destination) = wire.destination.as_mut() {
        destination.kind = BackupDestinationKind::S3 as i32;
        destination.s3_bucket = "maran-backups".to_owned();
        destination.s3_region = "eu-central-1".to_owned();
        destination.s3_access_key_id = "AKIAEXAMPLE".to_owned();
        destination.s3_secret_access_key = "s3cr3t".to_owned();
    }

    let error = ValidatedRestore::from_request(&wire, &account())
        .err()
        .unwrap_or_else(|| panic!("refused"));

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
}
