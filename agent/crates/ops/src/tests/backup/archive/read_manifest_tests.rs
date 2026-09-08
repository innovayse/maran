//! Reading the manifest out of an archive, and refusing the archives it
//! describes wrongly.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use tempfile::TempDir;

use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// A manifest for `account`, at `version`.
fn manifest(account: &str, version: u32) -> BackupManifest {
    BackupManifest {
        version,
        account: account.to_owned(),
        backup_id: "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b".to_owned(),
        created_at_unix: 1_767_225_600,
        home_bytes: 10,
        databases: Vec::new(),
        agent_version: "0.0.0".to_owned(),
    }
}

/// Reads the manifest the fake archive carries, for `account`.
fn read(carried: BackupManifest, account: &str) -> (TempDir, Result<BackupManifest, BackupError>) {
    let host = RecordingBackupHost::new();
    host.holds_manifest(carried);
    let scratch = TempDir::new().unwrap();

    let answer = read_manifest(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        scratch.path(),
        account,
    );

    (scratch, answer)
}

/// The manifest this agent wrote is read back — the inverse control for the two
/// refusals below.
#[test]
fn a_manifest_this_agent_understands_is_read_back() {
    let (_scratch, answer) = read(manifest("alice", MANIFEST_VERSION), "alice");

    assert_eq!(answer.unwrap().account, "alice");
}

/// A manifest version this agent does not know is refused, never read on the
/// fields it happens to recognise.
#[test]
fn a_manifest_version_this_agent_does_not_know_is_refused() {
    let (_scratch, answer) = read(manifest("alice", MANIFEST_VERSION + 1), "alice");

    assert_eq!(answer.unwrap_err(), BackupError::ManifestVersionUnknown);
}

/// An archive addressed to another account is refused.
#[test]
fn an_archive_belonging_to_another_account_is_refused() {
    let (_scratch, answer) = read(manifest("bob", MANIFEST_VERSION), "alice");

    assert_eq!(answer.unwrap_err(), BackupError::ManifestAccountMismatch);
}

/// The manifest is extracted as ROOT, because it decides which databases are
/// about to be dropped.
#[test]
fn the_manifest_is_extracted_root_side() {
    let host = RecordingBackupHost::new();
    host.holds_manifest(manifest("alice", MANIFEST_VERSION));
    let scratch = TempDir::new().unwrap();

    read_manifest(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        scratch.path(),
        "alice",
    )
    .unwrap();

    assert_eq!(
        host.extractions(),
        vec![(
            "manifest.json".to_owned(),
            crate::backup::model::extract_identity::ExtractIdentity::Root
        )]
    );
}
