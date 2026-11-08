//! Exporting a published artifact: what is sent, in what order, and that every way
//! the artifact can be wrong is a typed refusal before or while it is sent.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{hard_link, remove_file, write};
use std::os::unix::fs::symlink;
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::package::verify_package::verify_package;
use crate::backup::test_package::{CollectingSink, generated_key, sha256_of};

use super::*;

fn account() -> AccountName {
    AccountName::parse("alice").unwrap()
}

fn backup_id() -> BackupId {
    BackupId::parse("3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b").unwrap()
}

/// An artifact of `size` bytes that is not a sealed archive.
fn artifact_bytes(size: usize) -> Vec<u8> {
    (0..size).map(|index| (index % 251) as u8).collect()
}

/// The backup manifest the sidecar carries.
fn backup_manifest(account: &str) -> BackupManifest {
    BackupManifest {
        version: MANIFEST_VERSION,
        account: account.to_owned(),
        backup_id: backup_id().as_str().to_owned(),
        created_at_unix: 1_767_225_600,
        home_bytes: 10,
        databases: vec![ManifestDatabase {
            name: "alice_shop".to_owned(),
            bytes: 3,
            sha256: sha256_of(b"sql"),
        }],
        agent_version: "0.0.0".to_owned(),
    }
}

/// An account's backup directory holding a published artifact and its sidecar.
struct Fixture {
    /// The directory, removed with the fixture.
    directory: TempDir,
    /// The bytes the artifact holds.
    bytes: Vec<u8>,
}

impl Fixture {
    fn new(size: usize) -> Self {
        let fixture = Self {
            directory: TempDir::new().unwrap(),
            bytes: artifact_bytes(size),
        };
        write(fixture.artifact(), &fixture.bytes).unwrap();
        fixture.publish_summary(&BackupSummary::readable(
            backup_id().as_str().to_owned(),
            backup_manifest("alice"),
            fixture.bytes.len() as u64,
            sha256_of(&fixture.bytes),
        ));

        fixture
    }

    fn artifact(&self) -> PathBuf {
        self.directory.path().join(artifact_file_name(&backup_id()))
    }

    fn sidecar(&self) -> PathBuf {
        self.directory.path().join(sidecar_file_name(&backup_id()))
    }

    fn publish_summary(&self, summary: &BackupSummary) {
        write(self.sidecar(), serde_json::to_vec_pretty(summary).unwrap()).unwrap();
    }

    fn export(
        &self,
        key: &PackageSigningKey,
        sink: &mut CollectingSink,
    ) -> Result<u64, BackupError> {
        export_in(
            current_uid().unwrap(),
            self.directory.path(),
            &account(),
            &backup_id(),
            key,
            sink,
        )
    }
}

#[test]
fn the_signed_manifest_comes_first_and_the_artifact_follows_byte_for_byte() {
    let fixture = Fixture::new(1000);
    let key = generated_key();
    let mut sink = CollectingSink::new();

    let sent = fixture.export(&key, &mut sink).unwrap();

    assert_eq!(sent, 1000);
    assert_eq!(sink.bytes, fixture.bytes);
    let header = sink.header.unwrap();
    let manifest = verify_package(&header, &key.fingerprint()).unwrap();
    assert_eq!(manifest.account, "alice");
    assert_eq!(manifest.backup_id, backup_id().as_str());
    assert_eq!(manifest.artifact_bytes, 1000);
    assert_eq!(manifest.artifact_sha256, sha256_of(&fixture.bytes));
    assert_eq!(manifest.databases, vec!["alice_shop".to_owned()]);
}

#[test]
fn an_artifact_larger_than_a_chunk_is_sent_in_chunks_within_the_bound() {
    let size = CHUNK_BYTES * 2 + 17;
    let fixture = Fixture::new(size);
    let mut sink = CollectingSink::new();

    fixture.export(&generated_key(), &mut sink).unwrap();

    assert_eq!(sink.chunk_sizes, vec![CHUNK_BYTES, CHUNK_BYTES, 17]);
    assert_eq!(sink.bytes, fixture.bytes);
}

#[test]
fn an_artifact_changed_in_place_is_refused_when_its_digest_no_longer_matches() {
    let fixture = Fixture::new(500);
    let mut damaged = fixture.bytes.clone();
    damaged[100] ^= 0xff;
    write(fixture.artifact(), damaged).unwrap();

    assert_eq!(
        fixture.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::ChecksumMismatch)
    );
}

#[test]
fn an_artifact_of_another_size_than_recorded_is_refused_before_anything_is_sent() {
    let fixture = Fixture::new(500);
    write(fixture.artifact(), artifact_bytes(501)).unwrap();
    let mut sink = CollectingSink::new();

    assert_eq!(
        fixture.export(&generated_key(), &mut sink),
        Err(BackupError::ArtifactSizeMismatch)
    );
    assert!(sink.header.is_none());
}

#[test]
fn a_sealed_backup_is_refused_by_its_label_and_by_its_first_bytes() {
    let labelled = Fixture::new(500);
    labelled.publish_summary(
        &BackupSummary::readable(
            backup_id().as_str().to_owned(),
            backup_manifest("alice"),
            500,
            sha256_of(&labelled.bytes),
        )
        .sealed(),
    );
    assert_eq!(
        labelled.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::SealedArtifactNotTransferable)
    );

    let by_magic = Fixture::new(500);
    let mut sealed = b"MARANENC".to_vec();
    sealed.extend(artifact_bytes(492));
    write(by_magic.artifact(), &sealed).unwrap();
    by_magic.publish_summary(&BackupSummary::readable(
        backup_id().as_str().to_owned(),
        backup_manifest("alice"),
        500,
        sha256_of(&sealed),
    ));
    assert_eq!(
        by_magic.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::SealedArtifactNotTransferable)
    );
}

#[test]
fn a_missing_artifact_is_not_found_and_a_missing_sidecar_is_unreadable() {
    let fixture = Fixture::new(10);
    remove_file(fixture.sidecar()).unwrap();
    assert_eq!(
        fixture.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::SidecarUnreadable)
    );

    remove_file(fixture.artifact()).unwrap();
    assert_eq!(
        fixture.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::NotFound)
    );
}

#[test]
fn a_sidecar_naming_another_account_or_backup_is_refused() {
    let other_account = Fixture::new(10);
    other_account.publish_summary(&BackupSummary::readable(
        backup_id().as_str().to_owned(),
        backup_manifest("bobby"),
        10,
        sha256_of(&other_account.bytes),
    ));
    assert_eq!(
        other_account.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::ManifestAccountMismatch)
    );

    let other_backup = Fixture::new(10);
    let mut manifest = backup_manifest("alice");
    manifest.backup_id = "9b8a7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d".to_owned();
    other_backup.publish_summary(&BackupSummary::readable(
        backup_id().as_str().to_owned(),
        manifest,
        10,
        sha256_of(&other_backup.bytes),
    ));
    assert_eq!(
        other_backup.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::ManifestBackupMismatch)
    );
}

#[test]
fn a_link_a_second_name_or_a_foreign_owner_is_not_an_artifact() {
    let linked = Fixture::new(10);
    let target = linked.directory.path().join("elsewhere");
    write(&target, linked.bytes.clone()).unwrap();
    remove_file(linked.artifact()).unwrap();
    symlink(&target, linked.artifact()).unwrap();
    assert_eq!(
        linked.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::ArtifactNotRegularFile)
    );

    let twice = Fixture::new(10);
    hard_link(twice.artifact(), twice.directory.path().join("second-name")).unwrap();
    assert_eq!(
        twice.export(&generated_key(), &mut CollectingSink::new()),
        Err(BackupError::ArtifactNotRegularFile)
    );

    let foreign = Fixture::new(10);
    assert_eq!(
        export_in(
            current_uid().unwrap() + 1,
            foreign.directory.path(),
            &account(),
            &backup_id(),
            &generated_key(),
            &mut CollectingSink::new(),
        ),
        Err(BackupError::ArtifactNotRegularFile)
    );
}

#[test]
fn a_sink_that_stops_ends_the_export_with_nothing_further_read() {
    let fixture = Fixture::new(CHUNK_BYTES * 3);

    let mut refusing_header = CollectingSink::new();
    refusing_header.refuse_header = true;
    assert_eq!(
        fixture.export(&generated_key(), &mut refusing_header),
        Err(BackupError::ExportStopped)
    );
    assert!(refusing_header.bytes.is_empty());

    let mut stopping = CollectingSink::new();
    stopping.stop_at_chunk = Some(1);
    assert_eq!(
        fixture.export(&generated_key(), &mut stopping),
        Err(BackupError::ExportStopped)
    );
    assert_eq!(stopping.chunk_sizes, vec![CHUNK_BYTES]);
}
