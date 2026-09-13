//! One backup, whole or not at all.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{
    DirBuilder, File, Permissions, create_dir_all, read_to_string, set_permissions, write,
};
use std::os::unix::fs::{PermissionsExt as _, symlink};

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::db::database_name::DatabaseName;
use tempfile::TempDir;

use crate::accounts::take_account_lock;
use crate::backup::recording_backup_host::{
    ARCHIVE_PROGRAM, COMPRESSOR_PROGRAM, DUMP_PROGRAM, FIXED_NOW, RecordingBackupHost,
};

use super::*;

/// The catalog these tests drive, and whether it can answer at all.
struct FakeCatalog {
    /// The databases it names.
    names: Vec<DatabaseName>,
    /// Whether it refuses the question.
    refuses: bool,
}

impl DatabaseCatalog for FakeCatalog {
    fn databases_of(&self, _account: &AccountName) -> Result<Vec<DatabaseName>, BackupError> {
        if self.refuses {
            return Err(BackupError::DatabasesUnknown);
        }

        Ok(self.names.clone())
    }
}

/// A sink that keeps every report it was given.
struct RecordingSink {
    /// Stage and percentage, in order.
    reports: Vec<(BackupStage, u32)>,
}

impl ProgressSink for RecordingSink {
    fn report(&mut self, stage: BackupStage, percent: u32) {
        self.reports.push((stage, percent));
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

/// The directories one run works over, kept alive for the length of a test.
struct Fixture {
    /// The stand-in for `/home`.
    homes: TempDir,
    /// The stand-in for the agent's scratch root.
    scratch_root: TempDir,
    /// The stand-in for the configured backup root.
    backup_root: TempDir,
}

impl Fixture {
    /// Builds the three trees, including a home with a symlink out of it.
    fn new() -> Self {
        let homes = TempDir::new().expect("a temporary directory");
        let home = homes.path().join("alice");
        create_dir_all(home.join("public")).expect("the fixture home");
        write(home.join("public").join("index.php"), b"<?php").expect("the fixture file");
        // The fixture that a home of ordinary files could not see: a link out of
        // the home entirely, and one that walks back into it.
        symlink("/etc/shadow", home.join("secrets")).expect("the planted link");
        symlink("../alice/public", home.join("inside")).expect("the planted link");

        let scratch_root = TempDir::new().expect("a temporary directory");
        // The chain check open_scratch runs holds the scratch root to 0700, and
        // a TempDir is created with the umask's mode — 0775 under the umask
        // this workspace's tests run with. Set it here so the fixture presents
        // the same chain a real host presents, rather than weakening the check
        // to suit the fixture.
        set_permissions(scratch_root.path(), Permissions::from_mode(0o700))
            .expect("the scratch root's mode");
        let backup_root = TempDir::new().expect("a temporary directory");
        DirBuilder::new()
            .mode(0o700)
            .create(backup_root.path().join("alice"))
            .expect("the account's backup directory");

        Self {
            homes,
            scratch_root,
            backup_root,
        }
    }

    /// The account's backup directory.
    fn directory(&self) -> PathBuf {
        self.backup_root.path().join("alice")
    }

    /// Where this run stages its dumps.
    fn scratch(&self) -> PathBuf {
        self.scratch_root
            .path()
            .join("backup")
            .join(backup_id().as_str())
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

    /// The placement this run uses, with the ceilings a test can reach.
    fn placement(&self) -> Placement {
        Placement {
            home_root: self.homes.path().to_path_buf(),
            scratch_base: self.scratch_root.path().to_path_buf(),
            scratch_root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch(),
            owner: current_uid().expect("the current uid is readable"),
            dump_ceiling: 1024,
            archive_ceiling: 1024,
        }
    }
}

/// Runs one creation over `fixture` with `count` databases.
fn run(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    count: usize,
    sink: &mut RecordingSink,
) -> Result<BackupSummary, BackupError> {
    let catalog = FakeCatalog {
        names: databases(count),
        refuses: false,
    };

    create_in(
        &fixture.placement(),
        host,
        &catalog,
        &account(),
        &backup_id(),
        &fixture.directory(),
        sink,
    )
}

/// An empty sink to hand a run whose progress a test does not read.
fn sink() -> RecordingSink {
    RecordingSink {
        reports: Vec::new(),
    }
}

/// Every database the catalog names is dumped, and each dump is hashed.
#[test]
fn every_database_the_catalog_names_is_dumped_and_hashed() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    let summary = run(&fixture, &host, 3, &mut sink()).expect("the backup succeeds");

    assert_eq!(host.calls_to(DUMP_PROGRAM).len(), 3);
    assert_eq!(
        summary
            .readable_details()
            .expect("a readable summary")
            .manifest
            .databases
            .len(),
        3
    );
    for entry in &summary
        .readable_details()
        .expect("a readable summary")
        .manifest
        .databases
    {
        assert_eq!(entry.sha256.len(), 64, "{entry:?}");
        assert!(entry.bytes > 0, "{entry:?}");
    }
}

/// The manifest — the copy a listing reads — lists every dump with its digest.
#[test]
fn the_manifest_lists_every_dump_with_its_checksum() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    let summary = run(&fixture, &host, 2, &mut sink()).expect("the backup succeeds");

    let sidecar = read_to_string(fixture.sidecar()).expect("the sidecar");
    assert!(sidecar.contains("alice_shop0"), "{sidecar}");
    assert!(sidecar.contains("alice_shop1"), "{sidecar}");
    assert!(
        sidecar.contains(
            summary
                .readable_details()
                .expect("a readable summary")
                .artifact_sha256
                .as_str()
        ),
        "{sidecar}"
    );
    assert_eq!(
        summary
            .readable_details()
            .expect("a readable summary")
            .manifest
            .version,
        1
    );
    assert_eq!(
        summary
            .readable_details()
            .expect("a readable summary")
            .manifest
            .created_at_unix,
        FIXED_NOW
    );
    assert!(
        summary
            .readable_details()
            .expect("a readable summary")
            .manifest
            .home_bytes
            > 0
    );
}

/// The artifact appears under its published name only by a rename, so a reader
/// never sees a half-written archive.
#[test]
fn the_artifact_is_published_by_rename_so_a_reader_never_sees_a_partial() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    run(&fixture, &host, 1, &mut sink()).expect("the backup succeeds");

    let archive = host.calls_to(ARCHIVE_PROGRAM);
    let written = archive
        .first()
        .and_then(|call| {
            call.iter()
                .position(|argument| argument == "--file")
                .map(|index| call[index + 1].clone())
        })
        .expect("the archiver was told a file");
    assert!(
        written.ends_with(".tar.gz.partial"),
        "the archiver wrote the published name: {written}"
    );
    assert!(fixture.artifact().exists());
    assert!(!PathBuf::from(written).exists());
}

/// A dump that fails takes the whole backup with it: no artifact, no sidecar,
/// no scratch, and the customer's rows are not left on the disk.
#[test]
fn a_dump_failure_removes_the_scratch_and_publishes_no_artifact() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    host.fail_dumps_after_writing(2);

    let refusal = run(&fixture, &host, 2, &mut sink());

    assert!(matches!(
        refusal,
        Err(BackupError::DumpFailed { status: 2 })
    ));
    assert!(!fixture.artifact().exists());
    assert!(!fixture.sidecar().exists());
    assert!(!fixture.scratch().exists());
}

/// **The case a fake that only refused could not exhibit.** The client writes
/// real bytes and THEN exits non-zero; the partial dump must not survive into
/// anything, and no artifact may be published from it.
#[test]
fn a_dump_that_fails_after_writing_bytes_leaves_no_artifact_and_no_scratch() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    host.fail_dumps_after_writing(3);

    let refusal = run(&fixture, &host, 1, &mut sink());

    assert!(matches!(
        refusal,
        Err(BackupError::DumpFailed { status: 3 })
    ));
    let dump = fixture.scratch().join("databases").join("alice_shop0.sql");
    assert!(
        !dump.exists(),
        "the bytes the client wrote before failing are still on disk"
    );
    assert!(!fixture.scratch().exists());
    assert!(!fixture.artifact().exists());
    assert!(
        !fixture
            .directory()
            .join(format!("{}.tar.gz.partial", backup_id().as_str()))
            .exists()
    );
}

/// An archiver that refuses publishes nothing either.
#[test]
fn an_archive_failure_publishes_no_artifact() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    host.fail_archive(2);

    let refusal = run(&fixture, &host, 1, &mut sink());

    assert!(matches!(
        refusal,
        Err(BackupError::ArchiveFailed { status: 2 })
    ));
    assert!(!fixture.artifact().exists());
    assert!(!fixture.sidecar().exists());
    assert!(!fixture.scratch().exists());
}

/// A catalog that cannot answer stops the backup rather than producing one
/// whose manifest claims the account has no databases.
#[test]
fn a_catalog_that_cannot_answer_stops_the_backup() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    let catalog = FakeCatalog {
        names: Vec::new(),
        refuses: true,
    };

    let refusal = create_in(
        &fixture.placement(),
        &host,
        &catalog,
        &account(),
        &backup_id(),
        &fixture.directory(),
        &mut sink(),
    );

    assert!(matches!(refusal, Err(BackupError::DatabasesUnknown)));
    assert!(!fixture.artifact().exists());
}

/// A repeated creation of an id that is already published costs a `stat` and
/// takes no dump at all.
#[test]
fn an_existing_artifact_with_the_same_id_reports_already_exists_before_any_dump() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    File::create(fixture.artifact()).expect("the published artifact");

    let refusal = run(&fixture, &host, 2, &mut sink());

    assert!(matches!(refusal, Err(BackupError::AlreadyExists)));
    assert!(host.calls().is_empty(), "work was done before the check");
}

/// A second operation for one account is refused, not queued.
#[test]
fn a_second_backup_of_the_same_account_reports_already_running() {
    let account = AccountName::parse("busyone").expect("the fixture name is valid");
    let held = take_account_lock(&account);
    assert!(held.is_some());
    let host = RecordingBackupHost::new();
    let catalog = FakeCatalog {
        names: Vec::new(),
        refuses: false,
    };

    let refusal = create_backup(
        &host,
        &catalog,
        &account,
        &backup_id(),
        &LocalBackupRoot::default(),
        &mut sink(),
    );

    assert!(matches!(refusal, Err(BackupError::AlreadyRunning)));
    assert!(host.calls().is_empty());
}

/// The artifact and its sidecar are readable by their owner and by nobody else.
#[test]
fn the_artifact_and_the_sidecar_are_readable_by_their_owner_alone() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    run(&fixture, &host, 1, &mut sink()).expect("the backup succeeds");

    for path in [fixture.artifact(), fixture.sidecar()] {
        let metadata = path.symlink_metadata().expect("the published file");
        assert_eq!(
            metadata.mode() & 0o7777,
            0o600,
            "{path:?} is 0{:o}",
            metadata.mode() & 0o7777
        );
        assert_eq!(
            metadata.uid(),
            current_uid().expect("the current uid is readable")
        );
    }
}

/// Every byte in every argv came from a validated value, a path this area
/// built, or a constant flag. Nothing else reaches an argument position.
#[test]
fn no_caller_supplied_byte_reaches_an_argv_position_that_is_not_a_validated_type() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    run(&fixture, &host, 2, &mut sink()).expect("the backup succeeds");

    let scratch = fixture.scratch();
    let mut allowed: Vec<String> = vec![
        DUMP_PROGRAM.to_owned(),
        ARCHIVE_PROGRAM.to_owned(),
        "--single-transaction".to_owned(),
        "--quick".to_owned(),
        "--routines".to_owned(),
        "--triggers".to_owned(),
        "--events".to_owned(),
        "--hex-blob".to_owned(),
        "--databases".to_owned(),
        "--create".to_owned(),
        format!("--use-compress-program={COMPRESSOR_PROGRAM}"),
        "--numeric-owner".to_owned(),
        "--one-file-system".to_owned(),
        "--sparse".to_owned(),
        "--transform".to_owned(),
        r"s|^\.|home|S".to_owned(),
        "--file".to_owned(),
        "-C".to_owned(),
        ".".to_owned(),
        "databases".to_owned(),
        "manifest.json".to_owned(),
        // Paths, every one of them built here from AgentPaths' names, the
        // fixture's roots, a validated account name and a validated backup id.
        scratch.to_string_lossy().into_owned(),
        fixture
            .homes
            .path()
            .join("alice")
            .to_string_lossy()
            .into_owned(),
        fixture
            .directory()
            .join(format!("{}.tar.gz.partial", backup_id().as_str()))
            .to_string_lossy()
            .into_owned(),
    ];
    for database in databases(2) {
        allowed.push(database.as_str().to_owned());
        allowed.push(format!(
            "--result-file={}",
            scratch
                .join("databases")
                .join(format!("{}.sql", database.as_str()))
                .to_string_lossy()
        ));
    }

    // The vacuity guard, on the axis that can go blind. Every assertion below
    // is inside a loop over the recording, so a recording host that stopped
    // recording would make this test pass LOUDEST at the moment it stopped
    // looking — the shape rules/testing.md names ("if the thing I am checking
    // were broken, what would this line see?"). The guard is on the recording
    // itself and not on some neighbouring fact: `assert!(!calls.is_empty())`
    // proves there was something to inspect, and the dump count proves the
    // recording covers the calls this test is about rather than one stray
    // archive spawn.
    let calls = host.calls();
    assert!(
        !calls.is_empty(),
        "the recording host captured no argv at all, so the loop below would \
         assert nothing"
    );
    let dumps = calls
        .iter()
        .filter(|call| call.iter().any(|argument| argument == "--databases"))
        .count();
    assert_eq!(
        dumps, 2,
        "the recording must hold one dump per database for the provenance walk \
         to be about the arguments this test names"
    );

    for call in calls {
        for argument in call {
            assert!(
                allowed.contains(&argument),
                "an argument of unaccounted provenance reached an argv: {argument}"
            );
        }
    }
}

/// The percentages are computed from the work: two runs with different database
/// counts report different intermediate numbers, which a literal cannot do.
#[test]
fn progress_percent_is_computed_and_never_a_literal() {
    let two = Fixture::new();
    let five = Fixture::new();
    let mut two_reports = sink();
    let mut five_reports = sink();

    run(&two, &RecordingBackupHost::new(), 2, &mut two_reports).expect("the backup succeeds");
    run(&five, &RecordingBackupHost::new(), 5, &mut five_reports).expect("the backup succeeds");

    let after_first = |reports: &RecordingSink| {
        reports
            .reports
            .iter()
            .filter(|(stage, _)| *stage == BackupStage::DumpingDatabases)
            .nth(1)
            .map(|(_, percent)| *percent)
    };

    assert_eq!(after_first(&two_reports), Some(20));
    assert_eq!(after_first(&five_reports), Some(8));
    assert_ne!(after_first(&two_reports), after_first(&five_reports));
}

/// The archiving stage's own boundaries are reported around the archiver.
#[test]
fn the_archiving_stage_is_reported_around_the_archiver() {
    let fixture = Fixture::new();
    let mut reports = sink();

    run(&fixture, &RecordingBackupHost::new(), 1, &mut reports).expect("the backup succeeds");

    assert!(reports.reports.contains(&(BackupStage::ArchivingFiles, 40)));
    assert!(reports.reports.contains(&(BackupStage::ArchivingFiles, 80)));
    assert!(
        !reports
            .reports
            .iter()
            .any(|(stage, _)| *stage == BackupStage::Uploading),
        "a local creation reported an upload it did not do"
    );
}

/// **The artifact's own inode is checked, not the mode it was created with.**
/// A program handed a path can leave whatever it likes behind, and an artifact
/// anyone can read is not published at all — the rename would advertise a
/// readable copy of the customer's database as a backup.
#[test]
fn an_artifact_whose_mode_changed_under_the_archiver_is_not_published() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    host.loosen_artifact_mode(0o644);

    let refusal = run(&fixture, &host, 1, &mut sink());

    assert!(matches!(refusal, Err(BackupError::ArtifactUnpublishable)));
    assert!(!fixture.artifact().exists());
    assert!(!fixture.sidecar().exists());
}

/// The argv a whole backup really hands the archiver names its compressor by
/// absolute path — the assertion the artifact itself cannot make.
///
/// Deliberately at the OPERATION level and not only on the argv builder. The
/// builder's own test proves the shape; this one proves the shape survives the
/// path an actual `CreateBackup` takes, which is where a future refactor that
/// re-assembled an argv in the host would show up. Every other test in this
/// file passes identically with `--gzip` — a working archive is exactly what a
/// shadowed `gzip` also produces — so an assertion on the recorded arguments is
/// the only one that can tell the two apart.
#[test]
fn the_archiver_is_told_its_compressor_by_absolute_path_and_never_by_bare_name() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    run(&fixture, &host, 1, &mut sink()).expect("the backup succeeds");

    let archive_calls = host.calls_to(ARCHIVE_PROGRAM);
    assert_eq!(archive_calls.len(), 1, "{archive_calls:?}");
    let argv = &archive_calls[0];

    assert!(
        !argv.iter().any(|argument| argument == "--gzip"),
        "--gzip has tar fork /bin/sh -c \"gzip\" and resolve it on PATH: {argv:?}"
    );
    assert!(
        argv.iter()
            .any(|argument| argument == &format!("--use-compress-program={COMPRESSOR_PROGRAM}")),
        "{argv:?}"
    );
    assert!(COMPRESSOR_PROGRAM.starts_with('/'));
}

/// **F5, and the assertion that can actually see it.** A symlink pre-placed
/// where the sidecar goes is REFUSED, and the file it points at is never
/// written.
///
/// The sidecar is what `list_backups` and `restore_backup` read to decide what
/// a backup is, so a sidecar somebody else placed is a backup somebody else
/// describes. With `create` in place of `create_new` the open follows the link
/// and this creation succeeds, writing a JSON inventory of the customer's
/// databases into a path of the link author's choosing at whatever mode that
/// file already had — `mode` applies only to a file the open creates. Asserting
/// that the sidecar merely exists afterwards cannot tell those two apart; the
/// link's TARGET can.
#[test]
fn a_symlink_where_the_sidecar_goes_is_refused_and_never_followed() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    let elsewhere = TempDir::new().expect("a temporary directory");
    let target = elsewhere.path().join("stolen.json");
    symlink(&target, fixture.sidecar()).expect("the planted link");

    let refusal = run(&fixture, &host, 1, &mut sink());

    assert!(matches!(refusal, Err(BackupError::ArtifactUnpublishable)));
    assert!(
        target.symlink_metadata().is_err(),
        "the link was followed and its target written"
    );
}

/// A plain file already wearing the sidecar's name is refused for the same
/// reason and by the same line — the inverse of the symlink case, and the one
/// that shows the refusal is about the INODE not being ours rather than about
/// symlinks specifically. Its mode is left as it was, which is the harm
/// `create` would do: `mode` is ignored for a file that already exists, so the
/// customer's database inventory would land in a world-readable file.
#[test]
fn a_pre_placed_file_where_the_sidecar_goes_is_refused_and_left_alone() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();
    write(fixture.sidecar(), b"not ours").expect("the planted file");

    let refusal = run(&fixture, &host, 1, &mut sink());

    assert!(matches!(refusal, Err(BackupError::ArtifactUnpublishable)));
    assert_eq!(
        read_to_string(fixture.sidecar()).expect("the planted file survives"),
        "not ours"
    );
}

/// **The inverse control for both refusals above.** An ordinary run still
/// writes a sidecar, so those two are not passing because the sidecar write
/// refuses everything.
#[test]
fn an_ordinary_run_still_writes_its_sidecar() {
    let fixture = Fixture::new();
    let host = RecordingBackupHost::new();

    run(&fixture, &host, 1, &mut sink()).expect("the backup succeeds");

    assert!(
        fixture
            .sidecar()
            .symlink_metadata()
            .expect("the sidecar was written")
            .is_file()
    );
}

/// A chain the scratch may be staged in, built by the test and handed to
/// [`open_scratch`] exactly as `create_backup` hands it the real one.
///
/// The uid is the test's own rather than root's: no test can create a
/// root-owned directory, and a gate exercised only on input it must reject
/// passes just as well once it has been mutated into rejecting everything
/// (rules/testing.md). Every test below therefore has an accepting twin.
struct Chain {
    /// The temporary directory every level lives under. `0700`, so it stands
    /// in for a `/var/lib` nobody but root can write.
    base: TempDir,
}

impl Chain {
    /// A base with the mode a real ancestor must have.
    fn new() -> Self {
        let base = TempDir::new().expect("a temporary directory");
        set_permissions(base.path(), Permissions::from_mode(0o700)).expect("the base's mode");

        Self { base }
    }

    /// Creates a directory under the base with an explicit mode.
    fn make(&self, relative: &str, mode: u32) -> PathBuf {
        let path = self.base.path().join(relative);
        DirBuilder::new()
            .recursive(true)
            .create(&path)
            .expect("the planted directory");
        set_permissions(&path, Permissions::from_mode(mode)).expect("the planted mode");

        path
    }

    /// Runs the gate over `root`, staging `<root>/backup/an-id`.
    fn open(&self, root: &Path) -> Result<PathBuf, BackupError> {
        open_scratch(
            self.base.path(),
            root,
            &root.join("backup").join("an-id"),
            current_uid().expect("the current uid is readable"),
        )
    }
}

/// **The accepting control for every refusal below.** A chain that is sound at
/// every level is used, and the dumps' directory comes back `0700`.
///
/// Without this, all four refusals below are satisfied by a gate that refuses
/// unconditionally — which is what the escalation this closes would look like
/// if the fix went one step too far: no backup would ever run again.
#[test]
fn a_scratch_chain_that_is_sound_at_every_level_is_accepted() {
    let chain = Chain::new();
    let root = chain.make("maran-scratch", 0o700);

    let databases = chain.open(&root).expect("the sound chain is accepted");

    assert!(databases.starts_with(&root));
    assert_eq!(
        databases
            .symlink_metadata()
            .expect("the dumps' directory exists")
            .mode()
            & 0o7777,
        0o700
    );
}

/// The measured escalation, in the shape it was measured
/// (`docs/superpowers/notes/2026-09-05-backups-threat-note.md` §1): an
/// unprivileged uid owns a directory ABOVE the scratch, which is all it needs
/// to rename the level below aside and leave a symlink at that name. The modes
/// on the leaves were `0700` throughout and stopped nothing, because the
/// attacker never had to enter them.
#[test]
fn a_writable_directory_anywhere_above_the_scratch_root_is_refused() {
    let chain = Chain::new();
    chain.make("state", 0o777);
    let root = chain.make("state/maran-scratch", 0o700);

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// The same directory with the world-write bit off is accepted — the inverse
/// control that pins the refusal above to the WRITE bit and not to the depth of
/// the chain or to the extra level existing at all.
#[test]
fn an_unwritable_directory_above_the_scratch_root_is_accepted() {
    let chain = Chain::new();
    chain.make("state", 0o755);
    let root = chain.make("state/maran-scratch", 0o700);

    chain
        .open(&root)
        .expect("a readable ancestor is not a finding");
}

/// A symlink at the scratch root's own name is not a directory, whatever it
/// points at. `symlink_metadata` and not `metadata` is what makes this
/// observable: the target here is a directory that would pass every other test.
#[test]
fn a_symlink_standing_in_for_the_scratch_root_is_refused() {
    let chain = Chain::new();
    let target = chain.make("elsewhere", 0o700);
    let root = chain.base.path().join("maran-scratch");
    symlink(&target, &root).expect("the planted link");

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
    assert!(
        !target.join("backup").exists(),
        "nothing was created through the link"
    );
}

/// A scratch root that is root's but readable by others is refused too: this
/// directory holds full plaintext dumps of a customer's databases while a
/// backup runs, so "nobody can write it" is not the standard — nobody may read
/// it either.
#[test]
fn a_scratch_root_others_can_read_is_refused() {
    let chain = Chain::new();
    let root = chain.make("maran-scratch", 0o755);

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// A walk that cannot reach its leaf has checked nothing, and answering `Ok`
/// there would be a gate reporting on a path it never visited
/// (rules/testing.md: a check must be able to observe what it reports on).
#[test]
fn a_scratch_outside_the_base_the_walk_starts_from_is_refused() {
    let chain = Chain::new();
    let outside = TempDir::new().expect("a temporary directory");

    let refusal = open_scratch(
        chain.base.path(),
        outside.path(),
        &outside.path().join("backup").join("an-id"),
        current_uid().expect("the current uid is readable"),
    );

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// A regular file standing at the scratch root's name is refused.
///
/// **Stated honestly: this test does not pin the gate's `is_dir` check.**
/// Deleting that check was measured leaving this test green, because a file at
/// that name makes the creation below fail with `ENOTDIR` and the operation
/// refuses either way. The check stays because it says what the gate means and
/// because the outcome must not depend on a later call happening to fail; the
/// test stays because the OUTCOME — a refusal, and no dumps — is what a caller
/// depends on, and that is observable. No test in this suite can distinguish
/// the two, and a reviewer should not be told otherwise.
#[test]
fn a_regular_file_standing_in_for_the_scratch_root_is_refused() {
    let chain = Chain::new();
    let root = chain.base.path().join("maran-scratch");
    File::create(&root).expect("the planted file");
    set_permissions(&root, Permissions::from_mode(0o600)).expect("the planted mode");

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// A chain that is sound in every other way but belongs to somebody other than
/// the uid this operation expects is refused.
///
/// The expected owner is `ROOT_UID` on a real host and no test can create a
/// directory owned by root, so this asks the question the other way round: a
/// chain the test really owns, checked against an owner it is not. Without it
/// the ownership test is unobservable, and deleting it leaves the suite green —
/// measured.
#[test]
fn a_scratch_chain_owned_by_somebody_other_than_the_expected_uid_is_refused() {
    let chain = Chain::new();
    let root = chain.make("maran-scratch", 0o700);
    let owner = current_uid().expect("the current uid is readable");

    let refusal = open_scratch(
        chain.base.path(),
        &root,
        &root.join("backup").join("an-id"),
        owner + 1,
    );

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}
