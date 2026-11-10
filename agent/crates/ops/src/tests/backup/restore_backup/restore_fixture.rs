//! The fixture every restore test builds on: the four directory trees, the
//! archive and sidecar a run reads, the recording host, and the runners that
//! drive one restore over them.

use std::fs::{DirBuilder, Permissions, create_dir_all, set_permissions, write};
use std::os::unix::fs::{DirBuilderExt as _, MetadataExt as _, PermissionsExt as _};
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use sha2::{Digest as _, Sha256};
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::jail_mounts::JailMounts;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::recording_backup_host::{FIXED_NOW, RecordingBackupHost};
use crate::backup::recording_jail_mounts::RecordingJailMounts;

use super::super::*;

/// The bytes the fixture's artifact holds.
pub(super) const ARTIFACT_BYTES: &str = "gzip-bytes";

/// The file the account's live home holds before a restore.
pub(super) const LIVE_FILE: &str = "the customer's own file";

/// A sink that keeps every report it was given.
pub(super) struct RecordingSink {
    /// Stage and percentage, in order.
    pub(super) reports: Vec<(RestoreStage, u32)>,
}

impl RestoreSink for RecordingSink {
    fn report(&mut self, stage: RestoreStage, percent: u32) {
        self.reports.push((stage, percent));
    }
}

/// An empty sink to hand a run whose progress a test does not read.
pub(super) fn sink() -> RecordingSink {
    RecordingSink {
        reports: Vec::new(),
    }
}

/// The account every fixture belongs to.
pub(super) fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The backup id every fixture uses.
pub(super) fn backup_id() -> BackupId {
    BackupId::parse("3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b").expect("the fixture id is valid")
}

/// `count` databases of this account, named the way the panel names them.
pub(super) fn databases(count: usize) -> Vec<DatabaseName> {
    let account = account();
    (0..count)
        .map(|index| {
            DatabaseName::for_account(&account, &format!("shop{index}"))
                .expect("the fixture name is valid")
        })
        .collect()
}

/// The dump the fixture's archive carries for `database`.
pub(super) fn dump_of(database: &DatabaseName) -> String {
    format!("-- dump of {}\n", database.as_str())
}

/// The lowercase hex SHA-256 of `text`.
pub(super) fn digest(text: &str) -> String {
    let mut hasher = Sha256::new();
    hasher.update(text.as_bytes());
    hasher
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

/// The manifest the fixture's archive carries for `count` databases.
pub(super) fn manifest(count: usize) -> BackupManifest {
    BackupManifest {
        version: MANIFEST_VERSION,
        account: account().as_str().to_owned(),
        backup_id: backup_id().as_str().to_owned(),
        created_at_unix: FIXED_NOW,
        home_bytes: 10,
        databases: databases(count)
            .iter()
            .map(|database| ManifestDatabase {
                name: database.as_str().to_owned(),
                bytes: dump_of(database).len() as u64,
                sha256: digest(&dump_of(database)),
            })
            .collect(),
        agent_version: "0.0.0".to_owned(),
    }
}

/// The directories one run works over, kept alive for the length of a test.
pub(super) struct Fixture {
    /// The stand-in for `/home`.
    homes: TempDir,
    /// The stand-in for the agent's scratch root.
    scratch_root: TempDir,
    /// The stand-in for `/home/.maran-restore`.
    staging_root: TempDir,
    /// The stand-in for the configured backup root.
    backup_root: TempDir,
}

impl Fixture {
    /// Builds the four trees, a live home, and a published artifact with its
    /// sidecar.
    pub(super) fn new(count: usize) -> Self {
        let homes = TempDir::new().unwrap();
        let home = homes.path().join("alice");
        create_dir_all(&home).unwrap();
        write(home.join("live.txt"), LIVE_FILE.as_bytes()).unwrap();

        let fixture = Self {
            homes,
            scratch_root: TempDir::new().unwrap(),
            staging_root: TempDir::new().unwrap(),
            backup_root: TempDir::new().unwrap(),
        };

        // The chain check open_restore_scratch runs holds the scratch root to 0700, and
        // a TempDir is created with the umask's mode. Set it here so the
        // fixture presents the same chain a real host presents, rather than
        // weakening the check to suit the fixture.
        set_permissions(fixture.scratch_root.path(), Permissions::from_mode(0o700)).unwrap();

        DirBuilder::new()
            .mode(0o700)
            .create(fixture.directory())
            .unwrap();
        write(fixture.artifact(), ARTIFACT_BYTES.as_bytes()).unwrap();
        fixture.publish_sidecar(&manifest(count));

        fixture
    }

    /// Writes the sidecar describing `manifest` beside the artifact.
    pub(super) fn publish_sidecar(&self, manifest: &BackupManifest) {
        let summary = BackupSummary::readable(
            backup_id().as_str().to_owned(),
            manifest.clone(),
            ARTIFACT_BYTES.len() as u64,
            digest(ARTIFACT_BYTES),
        );
        write(self.sidecar(), serde_json::to_vec_pretty(&summary).unwrap()).unwrap();
    }

    /// Writes the sidecar of an artifact received from another host, the way an
    /// import publishes it: the same document, marked imported.
    pub(super) fn publish_imported_sidecar(&self, manifest: &BackupManifest) {
        let summary = BackupSummary::readable(
            backup_id().as_str().to_owned(),
            manifest.clone(),
            ARTIFACT_BYTES.len() as u64,
            digest(ARTIFACT_BYTES),
        )
        .imported();
        write(self.sidecar(), serde_json::to_vec_pretty(&summary).unwrap()).unwrap();
    }

    /// The account's backup directory.
    pub(super) fn directory(&self) -> PathBuf {
        self.backup_root.path().join("alice")
    }

    /// The published artifact's path.
    pub(super) fn artifact(&self) -> PathBuf {
        self.directory()
            .join(format!("{}.tar.gz", backup_id().as_str()))
    }

    /// The sidecar's path.
    pub(super) fn sidecar(&self) -> PathBuf {
        self.directory()
            .join(format!("{}.meta.json", backup_id().as_str()))
    }

    /// The account's live home.
    pub(super) fn home(&self) -> PathBuf {
        self.homes.path().join("alice")
    }

    /// Where this run stages its dumps.
    pub(super) fn scratch(&self) -> PathBuf {
        self.scratch_root
            .path()
            .join("backup")
            .join(backup_id().as_str())
    }

    /// Where this run builds the replacement home.
    pub(super) fn staging(&self) -> PathBuf {
        self.staging_root
            .path()
            .join(format!("alice.{}", backup_id().as_str()))
    }

    /// Where this run parks the home it replaced.
    pub(super) fn previous(&self) -> PathBuf {
        self.staging_root
            .path()
            .join(format!("alice.previous.{}", backup_id().as_str()))
    }

    /// Where this run writes the marker that lets a later process finish the
    /// swap it started.
    pub(super) fn marker(&self) -> PathBuf {
        self.staging_root
            .path()
            .join(format!("alice.{}.swap", backup_id().as_str()))
    }

    /// The identity the account resolves to for this fixture — the same pair
    /// the placement is built from, which is what makes a run under it the
    /// UNCHANGED case rather than an accident.
    pub(super) fn identity(&self) -> AccountIdentity {
        AccountIdentity {
            uid: current_uid().unwrap(),
            gid: self.homes.path().symlink_metadata().unwrap().gid(),
        }
    }

    /// The placement one run uses, owned by ids a test can really apply.
    pub(super) fn placement(&self) -> RestorePlacement {
        RestorePlacement {
            home_root: self.homes.path().to_path_buf(),
            scratch_base: self.scratch_root.path().to_path_buf(),
            scratch_root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch(),
            scratch_owner: current_uid().unwrap(),
            staging: self.staging(),
            previous: self.previous(),
            marker: self.marker(),
            owner: current_uid().unwrap(),
            account_group: self.homes.path().symlink_metadata().unwrap().gid(),
            group: self.homes.path().symlink_metadata().unwrap().gid(),
            floor: FreeSpaceFloor::from_mebibytes(1).unwrap(),
        }
    }
}

/// A host whose archive carries `count` databases and a manifest describing
/// them, and which resolves the account to the ids `fixture` places.
pub(super) fn host_with(fixture: &Fixture, count: usize) -> RecordingBackupHost {
    let host = RecordingBackupHost::new();
    // Stated, never defaulted: a restore re-asks the account's identity
    // immediately before it swaps the home, and a fake that answered something
    // plausible on its own would let every test below pass without any test
    // ever saying what the account is supposed to resolve to.
    host.answers_identities(&[Some(fixture.identity())]);
    host.holds_manifest(manifest(count));
    for database in databases(count) {
        host.holds_dump(database.as_str(), &dump_of(&database));
    }

    host
}

/// Runs one whole restore over `fixture`, with the panel allowing `allowed`
/// databases.
pub(super) fn run(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    allowed: &[DatabaseName],
    sink: &mut RecordingSink,
) -> Result<RestoreOutcome, BackupError> {
    run_scoped(fixture, host, allowed, &RestoreScope::Whole, sink)
}

/// Runs one whole restore over `fixture` with `jails` as the seam that mounts
/// the file-transfer jails onto the new home.
pub(super) fn run_with_jails(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    allowed: &[DatabaseName],
    jails: &dyn JailMounts,
    sink: &mut RecordingSink,
) -> Result<RestoreOutcome, BackupError> {
    restore_in(
        &fixture.placement(),
        host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        allowed,
        &RestoreScope::Whole,
        &digest(ARTIFACT_BYTES),
        None,
        jails,
        sink,
    )
}

/// Runs one restore of `scope` over `fixture`, with the panel allowing `allowed`
/// databases.
pub(super) fn run_scoped(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    allowed: &[DatabaseName],
    scope: &RestoreScope,
    sink: &mut RecordingSink,
) -> Result<RestoreOutcome, BackupError> {
    restore_in(
        &fixture.placement(),
        host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        allowed,
        scope,
        &digest(ARTIFACT_BYTES),
        None,
        &RecordingJailMounts::new(),
        sink,
    )
}

/// The rollback dumps reloaded during a run, in the order they were reloaded.
pub(super) fn rollback_loads(host: &RecordingBackupHost) -> Vec<String> {
    host.loaded()
        .into_iter()
        .filter(|(_name, path)| {
            path.components()
                .any(|component| component.as_os_str() == "rollback")
        })
        .map(|(name, _path)| name)
        .collect()
}
