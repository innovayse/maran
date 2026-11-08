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

    replace_database(&host, &database(), &path, DumpOrigin::Own).unwrap();

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

    let error = replace_database(&host, &database(), &path, DumpOrigin::Own).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: 1 });
    assert_eq!(host.dropped(), vec![database().as_str().to_owned()]);
}

/// A load killed at its deadline is a bound, not a load failure, and the
/// database is left dropped: the caller owes the same rollback it owes a load
/// that failed.
#[test]
fn a_load_that_times_out_is_a_bound_and_leaves_the_database_dropped() {
    let host = RecordingBackupHost::new();
    host.time_out_archive_load_for(database().as_str());
    let (_scratch, path) = dump();

    let error = replace_database(&host, &database(), &path, DumpOrigin::Own).unwrap_err();

    assert!(matches!(error, BackupError::Bound(_)), "{error:?}");
    assert_eq!(host.dropped(), vec![database().as_str().to_owned()]);
}

/// A clean dump of the fixture's database.
const CLEAN_DUMP: &str =
    "USE `alice_shop`;\nCREATE TABLE `t` (`a` int);\nINSERT INTO `t` VALUES (1);\n";

/// An imported dump is loaded by the restricted loader's temporary user and
/// never by the superuser client.
#[test]
fn an_imported_dump_is_loaded_through_the_restricted_loader_and_not_as_the_superuser() {
    let host = RecordingBackupHost::new();
    let (_scratch, path) = dump();
    write(&path, CLEAN_DUMP).unwrap();

    replace_database(&host, &database(), &path, DumpOrigin::Imported).unwrap();

    assert_eq!(host.dropped(), vec![database().as_str().to_owned()]);
    assert_eq!(
        host.restricted_calls(),
        vec![
            "create alice_shop".to_owned(),
            "load".to_owned(),
            "drop".to_owned()
        ]
    );
    assert_eq!(host.restricted_loaded(), vec![CLEAN_DUMP.to_owned()]);
    assert!(
        host.loaded().is_empty(),
        "the superuser client must not have been handed the imported dump"
    );
}

/// An imported dump that cannot be opened is refused before the drop, on the
/// recoverable side of the line, and the superuser is not tried instead.
#[test]
fn an_imported_dump_that_cannot_be_opened_is_refused_before_the_drop() {
    let host = RecordingBackupHost::new();
    let (scratch, _path) = dump();
    let missing = scratch.path().join("databases").join("absent.sql");

    let error = replace_database(&host, &database(), &missing, DumpOrigin::Imported).unwrap_err();

    assert!(matches!(error, BackupError::LoadFailed { .. }), "{error:?}");
    assert!(host.dropped().is_empty());
    assert!(host.restricted_calls().is_empty());
    assert!(host.loaded().is_empty());
}

/// A dump this host wrote is loaded exactly as before: by the superuser client,
/// with the restricted loader never consulted.
#[test]
fn an_own_dump_never_touches_the_restricted_loader() {
    let host = RecordingBackupHost::new();
    let (_scratch, path) = dump();

    replace_database(&host, &database(), &path, DumpOrigin::Own).unwrap();

    assert!(host.restricted_calls().is_empty());
    assert_eq!(host.loaded().len(), 1);
}
