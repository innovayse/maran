//! One dump comes out root-side, alone, and is checked before anything loads it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::validation::system::name::AccountName;
use sha2::{Digest as _, Sha256};
use tempfile::TempDir;

use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// The dump the wanted database carries.
const DUMP: &str = "-- CREATE DATABASE `alice_shop`;\n";

/// The dump a neighbouring database carries, which must never come out.
const OTHER_DUMP: &str = "-- CREATE DATABASE `alice_blog`;\n";

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The database a restore asks for.
fn wanted() -> DatabaseName {
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

/// A manifest entry claiming `sha256` for the wanted database.
fn entry(sha256: String) -> ManifestDatabase {
    ManifestDatabase {
        name: wanted().as_str().to_owned(),
        bytes: DUMP.len() as u64,
        sha256,
    }
}

/// Extracts the wanted database's dump out of an archive that also holds a
/// neighbour's, and checks it against `expected`.
fn extract(expected: ManifestDatabase) -> (TempDir, RecordingBackupHost, Result<(), BackupError>) {
    let host = RecordingBackupHost::new();
    host.holds_dump(wanted().as_str(), DUMP);
    host.holds_dump("alice_blog", OTHER_DUMP);
    let scratch = TempDir::new().unwrap();

    let answer = extract_database_as_root(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        scratch.path(),
        &wanted(),
        &expected,
    );

    (scratch, host, answer)
}

/// The inverse control: a dump matching its recorded digest is accepted.
#[test]
fn a_dump_matching_its_recorded_digest_is_accepted() {
    let (_scratch, _host, answer) = extract(entry(digest(DUMP)));

    assert_eq!(answer, Ok(()));
}

/// The dump is read by root and named alone: one member, and no neighbour's dump
/// reaches the scratch.
#[test]
fn only_the_wanted_dump_is_extracted_and_it_is_extracted_as_root() {
    let (scratch, host, _answer) = extract(entry(digest(DUMP)));

    assert_eq!(
        host.extractions(),
        vec![("databases/alice_shop.sql".to_owned(), ExtractIdentity::Root)]
    );
    assert!(
        scratch
            .path()
            .join("databases")
            .join("alice_shop.sql")
            .is_file()
    );
    assert!(
        !scratch
            .path()
            .join("databases")
            .join("alice_blog.sql")
            .exists(),
        "a dump the restore will never load is never written"
    );
}

/// A dump that is not the one the manifest recorded is refused, naming the
/// database, before anything could load it.
#[test]
fn a_dump_that_is_not_the_recorded_one_is_refused() {
    let (_scratch, _host, answer) = extract(entry(digest("-- some other dump\n")));

    assert_eq!(
        answer,
        Err(BackupError::DumpChecksumMismatch {
            database: "alice_shop".to_owned()
        })
    );
}
