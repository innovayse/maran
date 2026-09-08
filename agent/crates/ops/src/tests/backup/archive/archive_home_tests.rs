//! The manifest is in the scratch by the time the archiver reads it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::read_to_string;
use std::os::unix::fs::MetadataExt as _;
use std::path::PathBuf;

use tempfile::TempDir;

use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::recording_backup_host::{ARCHIVE_PROGRAM, RecordingBackupHost};

use super::*;

/// A manifest with one database in it.
fn manifest() -> BackupManifest {
    BackupManifest {
        version: 1,
        account: "alice".to_owned(),
        backup_id: "3f2a".to_owned(),
        created_at_unix: 1,
        home_bytes: 9,
        databases: vec![ManifestDatabase {
            name: "alice_shop".to_owned(),
            bytes: 8,
            sha256: "abc".to_owned(),
        }],
        agent_version: "0.1.0".to_owned(),
    }
}

/// The manifest lands in the scratch, and the archiver runs after it.
#[test]
fn the_manifest_is_written_into_the_scratch_before_the_archiver_runs() {
    let scratch = TempDir::new().expect("a temporary directory");
    let artifact = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();
    let spec = ArchiveSpec {
        home: PathBuf::from("/home/alice"),
        scratch: scratch.path().to_path_buf(),
        artifact: artifact.path().join("a.tar.gz.partial"),
    };

    archive_home(&host, &manifest(), &spec).expect("the archive succeeds");

    let written = read_to_string(scratch.path().join("manifest.json")).expect("the manifest");
    assert!(written.contains("\"account\": \"alice\""), "{written}");
    assert_eq!(host.calls_to(ARCHIVE_PROGRAM).len(), 1);
}

/// The manifest is root-only, like everything else this operation writes.
#[test]
fn the_manifest_is_written_readable_by_its_owner_alone() {
    let scratch = TempDir::new().expect("a temporary directory");
    let artifact = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();
    let spec = ArchiveSpec {
        home: PathBuf::from("/home/alice"),
        scratch: scratch.path().to_path_buf(),
        artifact: artifact.path().join("a.tar.gz.partial"),
    };

    archive_home(&host, &manifest(), &spec).expect("the archive succeeds");

    let mode = scratch
        .path()
        .join("manifest.json")
        .symlink_metadata()
        .expect("the manifest")
        .mode()
        & 0o7777;
    assert_eq!(mode, 0o600, "written 0{mode:o}");
}

/// An archiver that refuses is reported with its status.
#[test]
fn an_archiver_that_refuses_reports_its_status() {
    let scratch = TempDir::new().expect("a temporary directory");
    let artifact = TempDir::new().expect("a temporary directory");
    let host = RecordingBackupHost::new();
    host.fail_archive(2);
    let spec = ArchiveSpec {
        home: PathBuf::from("/home/alice"),
        scratch: scratch.path().to_path_buf(),
        artifact: artifact.path().join("a.tar.gz.partial"),
    };

    let refusal = archive_home(&host, &manifest(), &spec);

    assert!(matches!(
        refusal,
        Err(BackupError::ArchiveFailed { status: 2 })
    ));
}
