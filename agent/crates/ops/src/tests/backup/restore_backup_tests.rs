//! A restore, and what the account is if it stops halfway.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{DirBuilder, Permissions, create_dir_all, read_to_string, set_permissions, write};
use std::os::unix::fs::{MetadataExt as _, symlink};

use maran_agent_core::utils::current_uid::current_uid;
use sha2::{Digest as _, Sha256};
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::{
    ARCHIVE_PROGRAM, FIXED_NOW, LIST_PROGRAM, RecordingBackupHost,
};

use super::*;

/// The bytes the fixture's artifact holds.
const ARTIFACT_BYTES: &str = "gzip-bytes";

/// The file the account's live home holds before a restore.
const LIVE_FILE: &str = "the customer's own file";

/// A sink that keeps every report it was given.
struct RecordingSink {
    /// Stage and percentage, in order.
    reports: Vec<(RestoreStage, u32)>,
}

impl RestoreSink for RecordingSink {
    fn report(&mut self, stage: RestoreStage, percent: u32) {
        self.reports.push((stage, percent));
    }
}

/// An empty sink to hand a run whose progress a test does not read.
fn sink() -> RecordingSink {
    RecordingSink {
        reports: Vec::new(),
    }
}

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The backup id every fixture uses.
fn backup_id() -> BackupId {
    BackupId::parse("3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b").expect("the fixture id is valid")
}

/// `count` databases of this account, named the way the panel names them.
fn databases(count: usize) -> Vec<DatabaseName> {
    let account = account();
    (0..count)
        .map(|index| {
            DatabaseName::for_account(&account, &format!("shop{index}"))
                .expect("the fixture name is valid")
        })
        .collect()
}

/// The dump the fixture's archive carries for `database`.
fn dump_of(database: &DatabaseName) -> String {
    format!("-- dump of {}\n", database.as_str())
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

/// The manifest the fixture's archive carries for `count` databases.
fn manifest(count: usize) -> BackupManifest {
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
struct Fixture {
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
    fn new(count: usize) -> Self {
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

        // The chain check open_scratch runs holds the scratch root to 0700, and
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
    fn publish_sidecar(&self, manifest: &BackupManifest) {
        let summary = BackupSummary::readable(
            backup_id().as_str().to_owned(),
            manifest.clone(),
            ARTIFACT_BYTES.len() as u64,
            digest(ARTIFACT_BYTES),
        );
        write(self.sidecar(), serde_json::to_vec_pretty(&summary).unwrap()).unwrap();
    }

    /// The account's backup directory.
    fn directory(&self) -> PathBuf {
        self.backup_root.path().join("alice")
    }

    /// The published artifact's path.
    fn artifact(&self) -> PathBuf {
        self.directory()
            .join(format!("{}.tar.gz", backup_id().as_str()))
    }

    /// The sidecar's path.
    fn sidecar(&self) -> PathBuf {
        self.directory()
            .join(format!("{}.meta.json", backup_id().as_str()))
    }

    /// The account's live home.
    fn home(&self) -> PathBuf {
        self.homes.path().join("alice")
    }

    /// Where this run stages its dumps.
    fn scratch(&self) -> PathBuf {
        self.scratch_root
            .path()
            .join("backup")
            .join(backup_id().as_str())
    }

    /// Where this run builds the replacement home.
    fn staging(&self) -> PathBuf {
        self.staging_root
            .path()
            .join(format!("alice.{}", backup_id().as_str()))
    }

    /// Where this run parks the home it replaced.
    fn previous(&self) -> PathBuf {
        self.staging_root
            .path()
            .join(format!("alice.previous.{}", backup_id().as_str()))
    }

    /// The placement one run uses, owned by ids a test can really apply.
    fn placement(&self) -> Placement {
        Placement {
            home_root: self.homes.path().to_path_buf(),
            scratch_base: self.scratch_root.path().to_path_buf(),
            scratch_root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch(),
            scratch_owner: current_uid().unwrap(),
            staging: self.staging(),
            previous: self.previous(),
            owner: current_uid().unwrap(),
            account_group: self.homes.path().symlink_metadata().unwrap().gid(),
            group: self.homes.path().symlink_metadata().unwrap().gid(),
        }
    }
}

/// A host whose archive carries `count` databases and a manifest describing
/// them.
fn host_with(count: usize) -> RecordingBackupHost {
    let host = RecordingBackupHost::new();
    host.holds_manifest(manifest(count));
    for database in databases(count) {
        host.holds_dump(database.as_str(), &dump_of(&database));
    }

    host
}

/// Runs one restore over `fixture`, with the panel allowing `allowed`
/// databases.
fn run(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    allowed: &[DatabaseName],
    sink: &mut RecordingSink,
) -> Result<RestoreOutcome, BackupError> {
    restore_in(
        &fixture.placement(),
        host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        allowed,
        &digest(ARTIFACT_BYTES),
        sink,
    )
}

/// The rollback dumps reloaded during a run, in the order they were reloaded.
fn rollback_loads(host: &RecordingBackupHost) -> Vec<String> {
    host.loaded()
        .into_iter()
        .filter(|(_name, path)| {
            path.components()
                .any(|component| component.as_os_str() == "rollback")
        })
        .map(|(name, _path)| name)
        .collect()
}

/// A whole restore reports every database restored and the files swapped — the
/// inverse control, without which every refusal below would pass just as well
/// against an operation that refused everything.
#[test]
fn a_whole_restore_swaps_the_home_and_replaces_every_database() {
    let fixture = Fixture::new(2);
    let host = host_with(2);

    let outcome = run(&fixture, &host, &databases(2), &mut sink()).unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 2);
    assert_eq!(outcome.databases_total, 2);
    assert!(fixture.home().join("public").join("index.php").is_file());
    assert!(!fixture.home().join("live.txt").exists());
    assert!(!fixture.previous().exists());
    assert!(!fixture.scratch().exists());
}

/// A member the archive's layout has no place for is refused before anything at
/// all is unpacked.
#[test]
fn a_hostile_archive_member_outside_home_and_databases_is_refused_before_anything_is_touched() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.holds_members(&[
        "manifest.json",
        "home/",
        "home/public/index.php",
        "../../etc/cron.d/pwn",
    ]);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    match error {
        BackupError::UnexpectedArchiveMember { name } => {
            assert!(name.contains("cron.d/pwn"), "{name}");
        }
        other => panic!("expected a refused member, got {other:?}"),
    }
    // Not one member was unpacked — not even the ones that looked ordinary.
    assert!(host.extractions().is_empty());
    assert!(host.dropped().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// An artifact that is not the one the panel recorded is refused before the
/// archive is opened for any purpose — and therefore before the caller's
/// pre-restore backup would ever be worth taking.
#[test]
fn an_archive_whose_checksum_does_not_match_is_refused_before_the_pre_restore_backup() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let error = restore_in(
        &fixture.placement(),
        &host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        &databases(1),
        &digest("bytes this panel never took"),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
    assert!(host.calls_to(LIST_PROGRAM).is_empty());
    assert!(host.extractions().is_empty());
}

/// The cheap copy of the manifest cannot be edited into a restore: the copy
/// inside the archive is the authority and the two are compared.
#[test]
fn a_manifest_that_disagrees_with_the_sidecar_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let mut edited = manifest(1);
    edited.databases.clear();
    fixture.publish_sidecar(&edited);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
    assert!(host.dropped().is_empty());
}

/// **The case a naive parse-only check cannot see, reaching restore.** This
/// sidecar's JSON parses cleanly into today's `BackupSummary` shape — the
/// manifest and the digest are byte-for-byte the ones the archive and the
/// checksum agree on — and the only thing wrong with it is a `version` this
/// agent has never heard of. `list_backups` already refuses this exact
/// document as unreadable; before `require_sidecar_agrees` went through
/// `read_sidecar`, `restore_backup` accepted it, because it never looked at
/// `version` at all. It must now be refused here too.
#[test]
fn a_sidecar_naming_an_unknown_version_is_refused_even_when_it_otherwise_agrees() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let unknown_version = crate::backup::model::backup_summary::SUMMARY_VERSION + 1;

    let agreeing_but_unknown_version = serde_json::json!({
        "backup_id": backup_id().as_str(),
        "readable": true,
        "reason": null,
        "version": unknown_version,
        "manifest": manifest(1),
        "artifact_bytes": ARTIFACT_BYTES.len() as u64,
        "artifact_sha256": digest(ARTIFACT_BYTES),
    });
    write(
        fixture.sidecar(),
        serde_json::to_vec_pretty(&agreeing_but_unknown_version).unwrap(),
    )
    .unwrap();

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
    assert!(host.dropped().is_empty());
}

/// The inverse control a refusing gate needs: a sidecar at today's version,
/// agreeing with the manifest, still restores — the check above is a version
/// gate, not a check that now refuses everything.
#[test]
fn a_sidecar_at_the_current_version_still_restores() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let outcome = run(&fixture, &host, &databases(1), &mut sink()).unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 1);
}

/// A database the panel no longer knows about is refused, and nothing creates
/// it — an orphan owned by a user nothing points at is worse than a refusal.
#[test]
fn a_database_the_panel_does_not_know_is_refused_and_never_created() {
    let fixture = Fixture::new(2);
    let host = host_with(2);

    // The panel knows only the first of the two the archive carries.
    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    match error {
        BackupError::UnknownDatabase { name } => assert_eq!(name, "alice_shop1"),
        other => panic!("expected an unknown database, got {other:?}"),
    }
    assert!(host.dropped().is_empty());
    assert!(host.loaded().is_empty());
}

/// A dump that is not the one this backup wrote is refused BEFORE any drop,
/// because after the drop there is nothing left to refuse into.
#[test]
fn a_dump_whose_checksum_does_not_match_is_refused_before_any_drop() {
    let fixture = Fixture::new(1);
    let host = RecordingBackupHost::new();
    host.holds_manifest(manifest(1));
    host.holds_dump(databases(1)[0].as_str(), "not the dump this backup took");

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::DumpChecksumMismatch {
            database: "alice_shop0".to_owned()
        }
    );
    assert!(host.dropped().is_empty());
}

/// Anything that fails before the first drop is undone by doing nothing: the
/// home is the customer's, every database is untouched, and the operation's own
/// scratch and staging trees are gone.
#[test]
fn a_failure_before_the_first_drop_leaves_the_home_and_every_database_untouched() {
    let fixture = Fixture::new(1);
    let host = RecordingBackupHost::new();
    let mut future = manifest(1);
    future.version = MANIFEST_VERSION + 1;
    host.holds_manifest(future);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestVersionUnknown);
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(host.dropped().is_empty());
    assert!(!fixture.staging().exists());
    assert!(!fixture.scratch().exists());
}

/// A scratch that cannot hold the rollback dumps is refused before the first
/// drop, not discovered halfway through one.
///
/// The manifest's own byte counts are what the pre-flight adds up — they are
/// the only sizes known before the dumps are taken — so a manifest claiming
/// impossible dumps is how a unit test reaches a filesystem it cannot fill.
#[test]
fn a_scratch_too_small_for_the_rollback_dumps_is_refused_before_any_database_is_dropped() {
    let fixture = Fixture::new(2);
    let host = host_with(2);
    let mut enormous = manifest(2);
    for database in &mut enormous.databases {
        database.bytes = u64::MAX / 2;
    }
    host.holds_manifest(enormous.clone());
    fixture.publish_sidecar(&enormous);

    let error = run(&fixture, &host, &databases(2), &mut sink()).unwrap_err();

    assert!(
        matches!(error, BackupError::ScratchTooSmall { .. }),
        "the restore must refuse for want of room, answered: {error:?}"
    );
    // The property the refusal exists for: it happened while everything was
    // still there to refuse for.
    assert!(host.dropped().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// After the point of no return, every database already replaced is put back
/// from its own rollback dump, in reverse order.
#[test]
fn a_failure_after_the_first_drop_reloads_every_rollback_dump_in_reverse_order() {
    let fixture = Fixture::new(3);
    let host = host_with(3);
    host.fail_archive_load_for("alice_shop2");

    let error = run(&fixture, &host, &databases(3), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::RolledBack {
            failed: "alice_shop2".to_owned(),
            rolled_back: vec![
                "alice_shop2".to_owned(),
                "alice_shop1".to_owned(),
                "alice_shop0".to_owned()
            ]
        }
    );
    assert_eq!(
        rollback_loads(&host),
        vec![
            "alice_shop2".to_owned(),
            "alice_shop1".to_owned(),
            "alice_shop0".to_owned()
        ]
    );
    // The swap has not happened yet, so the files were never at risk.
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// A rollback that cannot put a database back names it, in its own error
/// variant, so it can never be read as the ordinary rollback.
#[test]
fn a_rollback_that_itself_fails_names_the_databases_it_could_not_restore() {
    let fixture = Fixture::new(3);
    let host = host_with(3);
    host.fail_archive_load_for("alice_shop2");
    host.fail_rollback_loads();

    let error = run(&fixture, &host, &databases(3), &mut sink()).unwrap_err();

    match error {
        BackupError::RolledBackPartially {
            failed,
            rolled_back,
            not_rolled_back,
        } => {
            assert_eq!(failed, "alice_shop2");
            // Every database this restore had replaced failed its rollback, so
            // the successful list is empty — and it is ASSERTED rather than
            // ignored: an empty `rolled_back` beside a full `not_rolled_back`
            // is the difference between "nothing could be put back" and "the
            // field is not being filled at all", which is exactly what a
            // mutation of the rollback bookkeeping produces.
            assert!(rolled_back.is_empty());
            assert_eq!(
                not_rolled_back,
                vec![
                    "alice_shop2".to_owned(),
                    "alice_shop1".to_owned(),
                    "alice_shop0".to_owned()
                ]
            );
        }
        other => panic!("expected a partial rollback, got {other:?}"),
    }
}

/// The second rename failing is reversed at once, and the account has its home
/// back at the path everything else on this host expects it at.
#[test]
fn a_failed_second_rename_reverses_the_first_and_the_account_has_its_home_back() {
    let fixture = Fixture::new(1);
    let placement = fixture.placement();
    // The staging tree was never built, so the second rename cannot succeed.
    assert!(!placement.staging.exists());

    let error = swap_home(&placement, &account(), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::StagingUnusable);
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!placement.previous.exists());
}

/// A restore that could not replace every database reports FAILED. There is no
/// success value that can describe four of five.
#[test]
fn a_restore_that_loses_a_database_reports_failed_not_completed() {
    let fixture = Fixture::new(3);
    let host = host_with(3);
    host.fail_archive_load_for("alice_shop1");

    let answer = run(&fixture, &host, &databases(3), &mut sink());

    assert!(
        answer.is_err(),
        "a restore that lost a database reported {answer:?}"
    );
    // And the whole restore, which is the only thing that may report an
    // outcome, reports counts that are equal.
    let whole = Fixture::new(3);
    let outcome = run(&whole, &host_with(3), &databases(3), &mut sink()).unwrap();
    assert_eq!(outcome.databases_restored, outcome.databases_total);
}

/// The home is extracted as the account and the dumps are not — R3 and R4, read
/// off the host's own log of who ran what.
#[test]
fn the_home_is_extracted_inside_fork_as_account_and_the_dumps_are_not() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    run(&fixture, &host, &databases(1), &mut sink()).unwrap();

    assert_eq!(
        host.extractions(),
        vec![
            ("manifest.json".to_owned(), ExtractIdentity::Root),
            ("databases".to_owned(), ExtractIdentity::Root),
            ("home".to_owned(), ExtractIdentity::Account(account())),
        ]
    );
}

/// Progress reports never go backwards, and everything whose failure leaves the
/// account untouched is reported under `verifying`.
#[test]
fn every_step_that_is_recoverable_by_doing_nothing_is_reported_as_verifying() {
    let fixture = Fixture::new(2);
    let host = host_with(2);
    let mut sink = sink();

    run(&fixture, &host, &databases(2), &mut sink).unwrap();

    let mut previous = 0;
    for (_stage, percent) in &sink.reports {
        assert!(*percent >= previous, "{:?}", sink.reports);
        previous = *percent;
    }
    let last_verifying = sink
        .reports
        .iter()
        .rposition(|(stage, _)| *stage == RestoreStage::Verifying)
        .expect("the verifying stage is reported");
    let first_drop_report = sink
        .reports
        .iter()
        .position(|(stage, _)| *stage == RestoreStage::RestoringDatabases)
        .expect("the database stage is reported");
    assert!(last_verifying < first_drop_report);
    // No archiver run is reported by a restore: it creates nothing.
    assert!(host.calls_to(ARCHIVE_PROGRAM).is_empty());
}

/// A chain a restore may stage in, built by the test and handed to
/// [`open_scratch`] exactly as [`restore_backup`] hands it the real one.
///
/// The uid is the test's own rather than root's: no test can create a
/// root-owned directory, and a gate exercised only on input it must reject
/// passes just as well once it has been mutated into rejecting everything
/// (rules/testing.md). Every refusal below therefore has an accepting twin.
///
/// The chain rule itself is one function shared with the creation side and is
/// pinned level by level in `create_backup_tests.rs`. What these four ask is
/// the question that suite cannot answer: whether a RESTORE runs it at all —
/// which it did not, for as long as this file had no such test, while the
/// creation side's suite stayed green.
struct RestoreChain {
    /// The temporary directory every level lives under. `0700`, so it stands
    /// in for a `/var/lib` nobody but root can write.
    base: TempDir,
}

impl RestoreChain {
    /// A base with the mode a real ancestor must have.
    fn new() -> Self {
        let base = TempDir::new().unwrap();
        set_permissions(base.path(), Permissions::from_mode(0o700)).unwrap();

        Self { base }
    }

    /// Creates a directory under the base with an explicit mode.
    fn make(&self, relative: &str, mode: u32) -> PathBuf {
        let path = self.base.path().join(relative);
        DirBuilder::new().recursive(true).create(&path).unwrap();
        set_permissions(&path, Permissions::from_mode(mode)).unwrap();

        path
    }

    /// Runs the gate over `root`, staging `<root>/backup/an-id`.
    fn open(&self, root: &Path) -> Result<PathBuf, BackupError> {
        open_scratch(
            self.base.path(),
            root,
            &root.join("backup").join("an-id"),
            current_uid().unwrap(),
        )
    }
}

/// **The accepting control for the three refusals below.** A sound chain is
/// used and the scratch comes back `0700`.
///
/// Without it, all three are satisfied by a gate that refuses unconditionally —
/// a restore that can never run again.
#[test]
fn a_restore_scratch_chain_that_is_sound_at_every_level_is_accepted() {
    let chain = RestoreChain::new();
    let root = chain.make("maran-scratch", 0o700);

    let scratch = chain.open(&root).expect("a sound chain is usable");

    assert_eq!(scratch, root.join("backup").join("an-id"));
    assert_eq!(scratch.symlink_metadata().unwrap().mode() & 0o7777, 0o700);
}

/// The measured escalation, against the restore: an unprivileged uid owning a
/// directory ABOVE the scratch is all it needs to rename the level below aside
/// and leave a symlink at that name. A restore extracts the archive's database
/// dumps into this directory as root and then loads them, so what that buys is
/// a plaintext copy of every database in the backup AND a say in what goes back
/// into the live one.
#[test]
fn a_writable_directory_above_the_restore_scratch_root_is_refused() {
    let chain = RestoreChain::new();
    chain.make("state", 0o777);
    let root = chain.make("state/maran-scratch", 0o700);

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// A symlink at the scratch root's own name is not a directory, whatever it
/// points at — and nothing is created through it.
#[test]
fn a_symlink_standing_in_for_the_restore_scratch_root_is_refused() {
    let chain = RestoreChain::new();
    let target = chain.make("elsewhere", 0o700);
    let root = chain.base.path().join("maran-scratch");
    symlink(&target, &root).unwrap();

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
    assert!(
        !target.join("backup").exists(),
        "nothing was created through the link"
    );
}

/// A chain sound in every other way but belonging to somebody other than the
/// uid the restore expects is refused.
///
/// The expected owner is root on a real host and no test can create a
/// root-owned directory, so this asks it the other way round: a chain the test
/// really owns, checked against an owner it is not.
#[test]
fn a_restore_scratch_chain_owned_by_somebody_else_is_refused() {
    let chain = RestoreChain::new();
    let root = chain.make("maran-scratch", 0o700);
    let owner = current_uid().unwrap();

    let refusal = open_scratch(
        chain.base.path(),
        &root,
        &root.join("backup").join("an-id"),
        owner + 1,
    );

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}
