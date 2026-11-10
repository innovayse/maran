//! Importing a package: it is verified before anything is touched, staged exactly
//! as signed, published under the source's id and marked imported.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, create_dir_all, read, set_permissions, write};
use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _};
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::package::package_signing_key::PackageSigningKey;
use crate::backup::package::transfer_manifest::TransferManifest;
use crate::backup::recording_backup_host::{FIXED_NOW, RecordingBackupHost};
use crate::backup::test_package::{ChunkSource, generated_key, manifest_for, sha256_of, signed};

use super::*;

const ID: &str = "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b";
const ARTIFACT: &[u8] = b"gzip-bytes";

fn account() -> AccountName {
    AccountName::parse("alice").unwrap()
}

fn backup_id() -> BackupId {
    BackupId::parse(ID).unwrap()
}

/// A host whose archive is the one the fixture's package describes: one database,
/// `alice_shop`.
fn host() -> RecordingBackupHost {
    let host = RecordingBackupHost::new();
    host.holds_members(&[
        "manifest.json",
        "home/",
        "databases/",
        "databases/alice_shop.sql",
    ]);
    host.holds_manifest(BackupManifest {
        version: MANIFEST_VERSION,
        account: "alice".to_owned(),
        backup_id: ID.to_owned(),
        created_at_unix: FIXED_NOW,
        home_bytes: 10,
        databases: vec![ManifestDatabase {
            name: "alice_shop".to_owned(),
            bytes: 3,
            sha256: sha256_of(b"sql"),
        }],
        agent_version: "0.0.0".to_owned(),
    });

    host
}

struct Fixture {
    /// The stand-in for the configured backup root, `0700`.
    backup_root: TempDir,
    /// The stand-in for the scratch root, `0700`.
    scratch_root: TempDir,
}

impl Fixture {
    fn new() -> Self {
        let fixture = Self {
            backup_root: TempDir::new().unwrap(),
            scratch_root: TempDir::new().unwrap(),
        };
        for root in [&fixture.backup_root, &fixture.scratch_root] {
            set_permissions(root.path(), Permissions::from_mode(0o700)).unwrap();
        }

        fixture
    }

    fn directory(&self) -> PathBuf {
        self.backup_root.path().join("alice")
    }

    fn artifact(&self) -> PathBuf {
        self.directory().join(artifact_file_name(&backup_id()))
    }

    fn sidecar(&self) -> PathBuf {
        self.directory().join(sidecar_file_name(&backup_id()))
    }

    fn scratch(&self, floor_mebibytes: u64) -> ImportScratch {
        ImportScratch {
            base: self.scratch_root.path().to_path_buf(),
            root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch_root.path().join("import").join(ID),
            owner: current_uid().unwrap(),
            floor: FreeSpaceFloor::from_mebibytes(floor_mebibytes).unwrap(),
        }
    }

    fn import(
        &self,
        host: &RecordingBackupHost,
        source: &mut ChunkSource,
        manifest: &TransferManifest,
    ) -> Result<ImportedBackup, BackupError> {
        import_in(
            host,
            &self.scratch(1),
            self.backup_root.path(),
            source,
            manifest,
            &account(),
            &backup_id(),
        )
    }
}

fn manifest(key: &PackageSigningKey) -> TransferManifest {
    manifest_for(key, "alice", ID, ARTIFACT, &["alice_shop".to_owned()])
}

#[test]
fn a_verified_package_is_published_under_the_source_id_and_marked_imported() {
    let fixture = Fixture::new();
    let key = generated_key();
    let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);

    let imported = fixture
        .import(&host(), &mut source, &manifest(&key))
        .unwrap();

    assert_eq!(imported.account, "alice");
    assert_eq!(imported.backup_id, ID);
    assert_eq!(imported.artifact_bytes, ARTIFACT.len() as u64);
    assert_eq!(imported.artifact_sha256, sha256_of(ARTIFACT));
    assert_eq!(imported.database_count, 1);
    assert!(!imported.already_present);
    assert_eq!(read(fixture.artifact()).unwrap(), ARTIFACT);
    assert_eq!(
        fixture.artifact().metadata().unwrap().mode() & 0o7777,
        0o600
    );

    let recorded = read_sidecar(&fixture.sidecar()).unwrap();
    assert!(recorded.imported);
    assert!(!recorded.encrypted);
    assert_eq!(recorded.artifact_sha256, sha256_of(ARTIFACT));
    assert_eq!(recorded.manifest.account, "alice");

    let mut partial = fixture.artifact().into_os_string();
    partial.push(".partial");
    assert!(!PathBuf::from(partial).exists(), "no stage is left");
    assert!(
        !fixture.scratch(1).scratch.exists(),
        "the scratch is removed on success"
    );
}

#[test]
fn a_taken_system_user_name_is_refused_before_anything_is_created() {
    let fixture = Fixture::new();
    let host = host();
    host.answers_identities(&[Some(AccountIdentity {
        uid: 1000,
        gid: 1000,
    })]);
    let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);

    assert_eq!(
        fixture.import(&host, &mut source, &manifest(&generated_key())),
        Err(BackupError::ImportAccountExists)
    );
    assert!(!fixture.directory().exists());
    assert_eq!(source.asked(), 0, "no byte was read");
}

#[test]
fn an_unreadable_password_database_refuses_the_import_before_anything_is_created() {
    let fixture = Fixture::new();
    let host = host();
    host.fails_account_lookup();
    let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);

    assert_eq!(
        fixture.import(&host, &mut source, &manifest(&generated_key())),
        Err(BackupError::ExtractionIdentityUnavailable)
    );
    assert!(!fixture.directory().exists());
    assert_eq!(source.asked(), 0, "no byte was read");
}

#[test]
fn a_published_package_converges_even_once_its_system_user_exists() {
    let fixture = Fixture::new();
    let key = generated_key();
    let package = manifest(&key);
    fixture
        .import(
            &host(),
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &package,
        )
        .unwrap();

    // The panel creates the account after the first import; a retry then finds the user.
    let after_account_creation = host();
    after_account_creation.answers_identities(&[Some(AccountIdentity {
        uid: 1000,
        gid: 1000,
    })]);
    let mut second = ChunkSource::clean(vec![ARTIFACT.to_vec()]);
    let again = fixture
        .import(&after_account_creation, &mut second, &package)
        .unwrap();

    assert!(again.already_present);
    assert_eq!(second.asked(), 0);
}

#[test]
fn the_same_package_twice_converges_without_reading_the_stream_again() {
    let fixture = Fixture::new();
    let key = generated_key();
    let package = manifest(&key);
    fixture
        .import(
            &host(),
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &package,
        )
        .unwrap();

    let mut second = ChunkSource::clean(vec![ARTIFACT.to_vec()]);
    let again = fixture.import(&host(), &mut second, &package).unwrap();

    assert!(again.already_present);
    assert_eq!(again.database_count, 1);
    assert_eq!(second.asked(), 0);
}

#[test]
fn an_id_already_published_with_another_digest_is_refused_and_left_alone() {
    let fixture = Fixture::new();
    let key = generated_key();
    fixture
        .import(
            &host(),
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &manifest(&key),
        )
        .unwrap();

    let other = manifest_for(
        &key,
        "alice",
        ID,
        b"other-bytes",
        &["alice_shop".to_owned()],
    );
    assert_eq!(
        fixture.import(
            &host(),
            &mut ChunkSource::clean(vec![b"other-bytes".to_vec()]),
            &other
        ),
        Err(BackupError::ImportBackupIdTaken)
    );
    assert_eq!(read(fixture.artifact()).unwrap(), ARTIFACT);
}

/// Publishes `ARTIFACT` through a first import, then replaces the artifact on disk with
/// `replacement` and leaves the sidecar, which still records the package, untouched; asks
/// for the same package again and answers the result.
fn retry_over_a_replaced_artifact(
    replacement: &[u8],
) -> (Fixture, Result<ImportedBackup, BackupError>) {
    let fixture = Fixture::new();
    let key = generated_key();
    let package = manifest(&key);
    fixture
        .import(
            &host(),
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &package,
        )
        .unwrap();
    write(fixture.artifact(), replacement).unwrap();

    let mut second = ChunkSource::clean(vec![ARTIFACT.to_vec()]);
    let answer = fixture.import(&host(), &mut second, &package);
    assert_eq!(second.asked(), 0, "the retry never reads the stream");
    (fixture, answer)
}

#[test]
fn a_sidecar_that_lies_about_a_replaced_artifact_is_not_an_already_present_import() {
    // Same length as the package's artifact, other bytes: only the digest can tell.
    let replacement = b"gzip-bytez";
    assert_eq!(replacement.len(), ARTIFACT.len());

    let (fixture, answer) = retry_over_a_replaced_artifact(replacement);

    assert_eq!(answer, Err(BackupError::ImportBackupIdTaken));
    assert_eq!(read(fixture.artifact()).unwrap(), replacement, "left alone");
}

#[test]
fn a_sidecar_that_lies_about_a_truncated_artifact_is_not_an_already_present_import() {
    let (_fixture, answer) = retry_over_a_replaced_artifact(&ARTIFACT[..4]);

    assert_eq!(answer, Err(BackupError::ImportBackupIdTaken));
}

#[test]
fn a_filesystem_that_would_drop_below_the_floor_is_refused_before_a_byte_is_written() {
    let fixture = Fixture::new();
    let host = host();
    // The artifact fits, but the room left after it is one byte under the 1 MiB floor.
    host.has_available(1024 * 1024 + ARTIFACT.len() as u64 - 1);
    let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);

    assert!(matches!(
        fixture.import(&host, &mut source, &manifest(&generated_key())),
        Err(BackupError::FreeSpace(_))
    ));
    assert!(!fixture.artifact().exists());
    assert_eq!(source.asked(), 0);
}

#[test]
fn exactly_the_floor_left_over_is_enough() {
    let fixture = Fixture::new();
    let host = host();
    host.has_available(1024 * 1024 + ARTIFACT.len() as u64);

    fixture
        .import(
            &host,
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &manifest(&generated_key()),
        )
        .unwrap();
}

#[test]
fn a_stream_that_does_not_match_the_signed_digest_publishes_nothing() {
    let fixture = Fixture::new();
    let mut source = ChunkSource::clean(vec![b"gzip-byteZ".to_vec()]);

    assert_eq!(
        fixture.import(&host(), &mut source, &manifest(&generated_key())),
        Err(BackupError::ImportDigestMismatch)
    );
    assert!(!fixture.artifact().exists());
    assert!(!fixture.sidecar().exists());
}

#[test]
fn a_broken_stream_publishes_nothing_and_leaves_no_stage() {
    let fixture = Fixture::new();
    let mut source = ChunkSource::interrupted(vec![ARTIFACT[..4].to_vec()]);

    assert_eq!(
        fixture.import(&host(), &mut source, &manifest(&generated_key())),
        Err(BackupError::ImportInterrupted)
    );
    let leftovers: Vec<_> = std::fs::read_dir(fixture.directory()).unwrap().collect();
    assert!(leftovers.is_empty());
}

#[test]
fn an_archive_the_member_scan_refuses_publishes_nothing_and_clears_the_scratch() {
    let fixture = Fixture::new();
    let host = host();
    host.holds_members(&["manifest.json", "home/", "databases/", "../escape"]);
    let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);

    assert!(matches!(
        fixture.import(&host, &mut source, &manifest(&generated_key())),
        Err(BackupError::UnexpectedArchiveMember { .. })
    ));
    assert!(!fixture.artifact().exists());
    assert!(!fixture.scratch(1).scratch.exists());
    assert!(
        std::fs::read_dir(fixture.directory())
            .unwrap()
            .next()
            .is_none()
    );
}

#[test]
fn an_unsafe_backup_directory_is_refused() {
    let fixture = Fixture::new();
    create_dir_all(fixture.directory()).unwrap();
    set_permissions(fixture.directory(), Permissions::from_mode(0o755)).unwrap();
    write(fixture.directory().join("note"), b"x").unwrap();

    assert!(matches!(
        fixture.import(
            &host(),
            &mut ChunkSource::clean(vec![ARTIFACT.to_vec()]),
            &manifest(&generated_key())
        ),
        Err(BackupError::BackupRootUnsafe { .. })
    ));
}

#[test]
fn a_package_is_verified_before_the_lock_the_root_or_the_stream_is_touched() {
    let key = generated_key();
    let package = manifest(&key);
    let good = signed(&key, &package);
    let root = LocalBackupRoot::default();
    let floor = FreeSpaceFloor::from_mebibytes(1).unwrap();

    let attempts: Vec<(&str, ImportRequest, BackupError)> = vec![
        (
            "unconfirmed fingerprint",
            ImportRequest {
                signed: good.clone(),
                expected_fingerprint: generated_key().fingerprint(),
                max_bytes: 0,
            },
            BackupError::PackageFingerprintMismatch,
        ),
        (
            "forged manifest",
            ImportRequest {
                signed: {
                    let mut forged = good.clone();
                    forged.manifest[5] ^= 1;
                    forged
                },
                expected_fingerprint: key.fingerprint(),
                max_bytes: 0,
            },
            BackupError::PackageSignatureInvalid,
        ),
        (
            "declared size over the caller's bound",
            ImportRequest {
                signed: good.clone(),
                expected_fingerprint: key.fingerprint(),
                max_bytes: 5,
            },
            BackupError::ImportTooLarge {
                declared: ARTIFACT.len() as u64,
                limit: 5,
            },
        ),
    ];

    for (label, request, expected) in attempts {
        let mut source = ChunkSource::clean(vec![ARTIFACT.to_vec()]);
        assert_eq!(
            import_backup(&host(), &request, &mut source, &root, &floor),
            Err(expected),
            "{label}"
        );
        assert_eq!(source.asked(), 0, "{label}: the stream was not read");
    }
}

#[test]
fn an_account_whose_lock_is_held_is_refused_without_waiting() {
    let key = generated_key();
    let request = ImportRequest {
        signed: signed(&key, &manifest(&key)),
        expected_fingerprint: key.fingerprint(),
        max_bytes: 0,
    };
    let floor = FreeSpaceFloor::from_mebibytes(1).unwrap();

    let held = take_account_lock(&account()).unwrap();
    let busy = import_backup(
        &host(),
        &request,
        &mut ChunkSource::clean(vec![]),
        &LocalBackupRoot::default(),
        &floor,
    );
    drop(held);

    // The inverse control is every other test in this file: with no lock held the
    // same package is imported through `import_in`.
    assert_eq!(busy, Err(BackupError::AlreadyRunning));
}
