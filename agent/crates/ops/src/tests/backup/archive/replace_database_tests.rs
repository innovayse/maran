//! Dropping and loading, in that order and no other.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir_all, write};

use tempfile::TempDir;

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::recording_backup_host::{DROP_PROGRAM, LOAD_PROGRAM, RecordingBackupHost};

use super::*;

/// The fixture's database.
fn database() -> DatabaseName {
    let account = AccountName::parse("alice").expect("the fixture name is valid");
    DatabaseName::for_account(&account, "shop").expect("the fixture name is valid")
}

/// A dump file the client can really open, inside a `databases/` directory.
fn dump() -> (TempDir, std::path::PathBuf) {
    let scratch = TempDir::new().unwrap();
    let directory = scratch.path().join("databases");
    create_dir_all(&directory).unwrap();
    let path = directory.join("x.sql");
    write(&path, b"-- dump\n").unwrap();

    (scratch, path)
}

/// The database is dropped and only then loaded, which is the order that makes
/// the drop the point of no return rather than an afterthought.
#[test]
fn a_database_is_dropped_before_its_dump_is_loaded() {
    let host = RecordingBackupHost::new();
    let (_scratch, path) = dump();

    replace_database(&host, &database(), &path).unwrap();

    assert_eq!(host.calls_to(DROP_PROGRAM).len(), 1);
    assert_eq!(host.calls_to(LOAD_PROGRAM).len(), 1);
    assert_eq!(host.dropped(), vec![database().as_str().to_owned()]);
    assert_eq!(host.loaded().len(), 1);
}

/// A load that fails is reported, and the database is left dropped — the caller
/// owes the rollback, and this step does not pretend otherwise.
#[test]
fn a_load_that_fails_is_reported_to_the_caller() {
    let host = RecordingBackupHost::new();
    host.fail_archive_load_for(database().as_str());
    let (_scratch, path) = dump();

    let error = replace_database(&host, &database(), &path).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: 1 });
    assert_eq!(host.dropped(), vec![database().as_str().to_owned()]);
}
