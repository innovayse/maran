//! Judging a staged archive against the signed manifest.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, set_permissions, write};
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use std::os::unix::fs::PermissionsExt as _;
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::recording_backup_host::{FIXED_NOW, RecordingBackupHost};
use crate::backup::test_package::{generated_key, manifest_for, sha256_of};

use super::*;

const ID: &str = "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b";

fn account() -> AccountName {
    AccountName::parse("alice").unwrap()
}

fn backup_id() -> BackupId {
    BackupId::parse(ID).unwrap()
}

/// The archive's own manifest naming `databases`.
fn inner_manifest(databases: &[&str]) -> BackupManifest {
    BackupManifest {
        version: MANIFEST_VERSION,
        account: "alice".to_owned(),
        backup_id: ID.to_owned(),
        created_at_unix: FIXED_NOW,
        home_bytes: 10,
        databases: databases
            .iter()
            .map(|name| ManifestDatabase {
                name: (*name).to_owned(),
                bytes: 3,
                sha256: sha256_of(b"sql"),
            })
            .collect(),
        agent_version: "0.0.0".to_owned(),
    }
}

/// The member names of an archive holding a dump per name.
fn members(databases: &[&str]) -> Vec<String> {
    let mut names = vec![
        "manifest.json".to_owned(),
        "home/".to_owned(),
        "databases/".to_owned(),
    ];
    names.extend(databases.iter().map(|name| format!("databases/{name}.sql")));
    names
}

struct Fixture {
    /// The scratch root, `0700`.
    scratch_root: TempDir,
    /// Where the staged archive lies.
    staged_directory: TempDir,
}

impl Fixture {
    fn new() -> Self {
        let fixture = Self {
            scratch_root: TempDir::new().unwrap(),
            staged_directory: TempDir::new().unwrap(),
        };
        set_permissions(fixture.scratch_root.path(), Permissions::from_mode(0o700)).unwrap();
        write(fixture.staged(), b"gzip-bytes").unwrap();

        fixture
    }

    fn staged(&self) -> PathBuf {
        self.staged_directory.path().join("x.partial")
    }

    fn scratch(&self) -> ImportScratch {
        ImportScratch {
            base: self.scratch_root.path().to_path_buf(),
            root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch_root.path().join("import").join(ID),
            owner: current_uid().unwrap(),
            floor: FreeSpaceFloor::from_mebibytes(1).unwrap(),
        }
    }

    fn judge(
        &self,
        host: &RecordingBackupHost,
        signed_databases: &[&str],
    ) -> Result<BackupManifest, BackupError> {
        let signed = manifest_for(
            &generated_key(),
            "alice",
            ID,
            b"gzip-bytes",
            &signed_databases
                .iter()
                .map(|name| (*name).to_owned())
                .collect::<Vec<_>>(),
        );

        judge_staged_import(
            host,
            &self.scratch(),
            &self.staged(),
            &signed,
            &account(),
            &backup_id(),
        )
    }
}

fn host_holding(databases: &[&str]) -> RecordingBackupHost {
    let host = RecordingBackupHost::new();
    let names = members(databases);
    host.holds_members(&names.iter().map(String::as_str).collect::<Vec<_>>());
    host.holds_manifest(inner_manifest(databases));

    host
}

#[test]
fn an_archive_that_matches_what_was_signed_is_accepted() {
    let fixture = Fixture::new();
    let host = host_holding(&["alice_shop"]);

    let inner = fixture.judge(&host, &["alice_shop"]).unwrap();

    assert_eq!(inner.account, "alice");
    assert_eq!(inner.databases.len(), 1);
}

#[test]
fn a_sealed_archive_is_refused_before_anything_is_listed() {
    let fixture = Fixture::new();
    write(fixture.staged(), b"MARANENC-and-the-rest").unwrap();
    let host = host_holding(&[]);

    assert_eq!(
        fixture.judge(&host, &[]),
        Err(BackupError::SealedArtifactNotTransferable)
    );
    assert!(host.calls().is_empty());
}

#[test]
fn a_member_outside_the_fixed_layout_is_refused() {
    let fixture = Fixture::new();
    let host = host_holding(&[]);
    host.holds_members(&["manifest.json", "home/", "databases/", "etc/passwd"]);

    assert!(matches!(
        fixture.judge(&host, &[]),
        Err(BackupError::UnexpectedArchiveMember { .. })
    ));
}

#[test]
fn an_archive_naming_another_backup_than_the_signed_one_is_refused() {
    let fixture = Fixture::new();
    let host = host_holding(&[]);
    let mut other = inner_manifest(&[]);
    other.backup_id = "9b8a7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d".to_owned();
    host.holds_manifest(other);

    assert_eq!(
        fixture.judge(&host, &[]),
        Err(BackupError::ManifestBackupMismatch)
    );
}

#[test]
fn an_archive_naming_another_account_is_refused() {
    let fixture = Fixture::new();
    let host = host_holding(&[]);
    let mut other = inner_manifest(&[]);
    other.account = "bobby".to_owned();
    host.holds_manifest(other);

    assert_eq!(
        fixture.judge(&host, &[]),
        Err(BackupError::ManifestAccountMismatch)
    );
}

#[test]
fn an_archive_whose_databases_differ_from_the_signed_ones_is_refused() {
    let fixture = Fixture::new();

    let host = host_holding(&["alice_shop", "alice_extra"]);
    assert_eq!(
        fixture.judge(&host, &["alice_shop"]),
        Err(BackupError::ManifestDisagreesWithSidecar)
    );

    let host = host_holding(&["alice_shop"]);
    assert_eq!(
        fixture.judge(&host, &["alice_shop", "alice_extra"]),
        Err(BackupError::ManifestDisagreesWithSidecar)
    );
}

#[test]
fn a_named_database_with_no_dump_in_the_archive_is_refused() {
    let fixture = Fixture::new();
    let host = host_holding(&["alice_shop"]);
    host.holds_members(&["manifest.json", "home/", "databases/"]);

    assert_eq!(
        fixture.judge(&host, &["alice_shop"]),
        Err(BackupError::DumpMissing)
    );
}

#[test]
fn an_archive_with_no_readable_manifest_is_refused() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    assert_eq!(
        fixture.judge(&host, &[]),
        Err(BackupError::ManifestUnreadable)
    );
}
