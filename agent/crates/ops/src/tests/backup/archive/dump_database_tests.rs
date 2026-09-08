//! What a dump runs with, and what is recorded about what it produced.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tempfile::TempDir;

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::recording_backup_host::{DUMP_PROGRAM, RecordingBackupHost};

use super::*;

/// A database name of the shape this product creates.
fn database() -> DatabaseName {
    let account = AccountName::parse("alice").expect("the fixture name is valid");
    DatabaseName::for_account(&account, "shop").expect("the fixture name is valid")
}

/// The dump runs with every flag a consistent, complete dump needs.
#[test]
fn the_dump_argv_carries_the_flags_a_consistent_dump_needs() {
    let arguments = dump_arguments(&database(), Path::new("/run/scratch/databases/x.sql"));

    for flag in [
        "--single-transaction",
        "--quick",
        "--routines",
        "--triggers",
        "--events",
        "--hex-blob",
        "--databases",
    ] {
        assert!(
            arguments.iter().any(|argument| argument == flag),
            "missing {flag}"
        );
    }
}

/// The dump reaches its file through the client's own flag, never a redirect —
/// there is no shell here to redirect with.
#[test]
fn the_dump_file_is_named_by_the_clients_own_flag() {
    let arguments = dump_arguments(&database(), Path::new("/run/scratch/databases/x.sql"));

    assert!(
        arguments
            .iter()
            .any(|argument| argument == "--result-file=/run/scratch/databases/x.sql")
    );
    assert!(!arguments.iter().any(|argument| argument.contains('>')));
}

/// The dump's file is named after the database, inside `databases/`.
#[test]
fn the_dump_is_named_after_its_database() {
    let path = dump_path(Path::new("/run/scratch/databases"), &database());

    assert_eq!(path, Path::new("/run/scratch/databases/alice_shop.sql"));
}

/// What the manifest records is measured from the file that landed.
#[test]
fn the_dump_is_hashed_from_the_file_that_landed() {
    let scratch = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();

    let entry = dump_database(&host, &database(), scratch.path(), 1024).expect("the dump succeeds");

    assert_eq!(entry.name, "alice_shop");
    assert_eq!(entry.bytes, 8);
    assert_eq!(entry.sha256.len(), 64);
    assert_eq!(host.calls_to(DUMP_PROGRAM).len(), 1);
}

/// A dump over the ceiling is refused before the next one is taken.
#[test]
fn a_dump_over_the_ceiling_is_refused() {
    let scratch = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();

    let refusal = dump_database(&host, &database(), scratch.path(), 2);

    assert!(matches!(
        refusal,
        Err(BackupError::DumpTooLarge {
            limit: 2,
            actual: 8
        })
    ));
}

/// A client that fails is reported with its status and nothing else.
#[test]
fn a_client_that_fails_reports_its_status_alone() {
    let scratch = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();
    host.fail_dumps_after_writing(2);

    let refusal = dump_database(&host, &database(), scratch.path(), 1024);

    assert!(matches!(
        refusal,
        Err(BackupError::DumpFailed { status: 2 })
    ));
}
