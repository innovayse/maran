//! The dumps come out root-side, and each is checked before anything loads it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::validation::system::name::AccountName;
use sha2::{Digest as _, Sha256};
use tempfile::TempDir;

use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// The dump every fixture carries.
const DUMP: &str = "-- CREATE DATABASE `alice_shop`;\n";

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The fixture's one database.
fn database() -> DatabaseName {
    DatabaseName::for_account(&account(), "shop").expect("the fixture name is valid")
}

/// The lowercase hex SHA-256 of `text`.
fn digest(text: &str) -> String {
    let mut hasher = Sha256::new();
    hasher.update(text.as_bytes());
    hasher
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

/// A manifest entry claiming `sha256` for the fixture's database.
fn entry(sha256: String) -> ManifestDatabase {
    ManifestDatabase {
        name: database().as_str().to_owned(),
        bytes: DUMP.len() as u64,
        sha256,
    }
}

/// Extracts the fixture's dumps and checks them against `expected`.
fn extract(expected: ManifestDatabase) -> (TempDir, RecordingBackupHost, Result<(), BackupError>) {
    let host = RecordingBackupHost::new();
    host.holds_dump(database().as_str(), DUMP);
    let scratch = TempDir::new().unwrap();

    let answer = extract_databases_as_root(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        scratch.path(),
        &[(database(), expected)],
    );

    (scratch, host, answer)
}

/// A dump matching the manifest is accepted, and it lands where the loader will
/// look for it — the inverse control for the refusal below.
#[test]
fn a_dump_matching_the_manifest_is_accepted_and_lands_in_the_scratch() {
    let (scratch, _host, answer) = extract(entry(digest(DUMP)));

    answer.unwrap();
    assert!(extracted_dump_path(scratch.path(), &database()).is_file());
}

/// A dump that is not the one this backup wrote is refused, and the refusal
/// names the database.
#[test]
fn a_dump_that_does_not_match_the_manifest_is_refused() {
    let (_scratch, _host, answer) = extract(entry(digest("something else entirely")));

    assert_eq!(
        answer.unwrap_err(),
        BackupError::DumpChecksumMismatch {
            database: database().as_str().to_owned()
        }
    );
}

/// The dumps are extracted as ROOT, because the loader connects as the database
/// superuser (R4).
#[test]
fn the_dumps_are_extracted_root_side() {
    let (_scratch, host, _answer) = extract(entry(digest(DUMP)));

    assert_eq!(
        host.extractions(),
        vec![("databases".to_owned(), ExtractIdentity::Root)]
    );
}
