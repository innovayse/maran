//! A restore killed mid-flight, and the start that has to put the account back.
//!
//! This suite exists for privileges audit finding F-2, and it is written the way
//! it is because no in-process test could observe the defect. `swap_home` parks
//! `/home/<account>` at `AgentPaths::restore_previous_dir` and renames the
//! staging tree into its place; every recovery in `restore_backup.rs` is
//! in-process, so a `SIGKILL` between those two renames — a `systemctl stop`
//! that outran `TimeoutStopSec`, an OOM kill, a power cut — executed none of
//! them and left the account with **no home directory at all**, permanently,
//! because nothing read the staging root at startup.
//!
//! So the cases here kill a real process with a real signal 9, at a real moment
//! inside that window, and then run the reconciliation the daemon runs before it
//! binds its socket, and assert the customer's home came back with the right
//! bytes and the right ownership.
//!
//! # How the kill is aimed without a hook in production code
//!
//! Through the seam that was already there. A restore reports progress to a
//! `RestoreSink` the caller supplies, and `swap_home` reports once BETWEEN its
//! two renames — the one moment in the whole operation at which the account has
//! no home, and a stage boundary an operator watching a stalled restore needs
//! for its own sake. A sink in this file kills its own process when that report
//! arrives. There is no test-only branch, no environment variable read by the
//! agent, and no injected failure: the production code does exactly what it does
//! on a customer's server, and the test is a caller that dies inside it.
//!
//! # The two halves are two processes
//!
//! They have to be: the thing under test is what survives a process that stops
//! existing. The parent creates the account, the database and the backup and
//! holds them for cleanup; the child does nothing but the restore, and dies. The
//! parent then asserts the window really was entered before it asserts anything
//! about recovery — a kill that missed must fail the case, not pass it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// `run_as` — the fixture's "can this credential still log in" probe — belongs to
// the database suite's claims and is not one of this suite's, so it is unused
// here. The allow is on the module rather than on the fixture, so a fixture item
// no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_mariadb.rs"]
mod polygon_mariadb;

use std::fs::{create_dir_all, metadata, read_dir, read_to_string, remove_dir_all, write};
use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use std::sync::Arc;

use maran_agent::services::backup::db_host_catalog::DbHostCatalog;
use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::group_id::GroupId;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::backup::{
    BackupStage, ProcessBackupHost, ProgressSink, RestoreSink, RestoreStage, create_backup,
    recover_restores, restore_backup,
};
use maran_ops::db::{CreateDatabaseRequest, ProcessDbHost, create_database};

use polygon_account::PolygonAccount;
use polygon_mariadb::PolygonMariadb;

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The variable that tells a re-entered process it is the child half, and which
/// account it is to restore.
const CHILD_ACCOUNT: &str = "MARAN_TEST_RESTORE_ACCOUNT";

/// The variable carrying the artifact digest the child must verify against.
const CHILD_DIGEST: &str = "MARAN_TEST_RESTORE_DIGEST";

/// The variable naming where in the restore the child is to kill itself.
const CHILD_KILL_AT: &str = "MARAN_TEST_RESTORE_KILL_AT";

/// Kill the process between the two renames of the home swap.
const KILL_IN_THE_SWAP: &str = "swap";

/// Kill the process once a rollback dump has been written and its database
/// replaced.
const KILL_AFTER_A_DUMP: &str = "dumps";

/// The password the fixture database's user is created with.
const FIXTURE_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The id every backup in these cases is created under.
const FIXTURE_BACKUP_ID: &str = "3f2504e0-4f89-41d3-9a0c-0305e82c3302";

/// What the home holds in the backup, and therefore what a restore must put
/// back. The case asserts this VALUE — a home that merely exists proves nothing
/// about which of the two trees ended up at the account's name.
const BACKED_UP_BYTES: &str = "the bytes the backup holds\n";

/// What the home holds after the customer has worked on since. A recovery that
/// rolled back instead of finishing forward leaves this.
const LIVE_BYTES: &str = "the bytes the customer wrote afterwards\n";

/// The note the fixture database holds when the backup is taken.
const NOTE_IN_THE_BACKUP: &str = "before-the-backup";

/// The note the fixture database holds when the restore starts.
const NOTE_BEFORE_THE_RESTORE: &str = "after-the-backup";

/// The adapter for the family this container actually is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The backup id these cases use, validated.
fn fixture_id() -> BackupId {
    BackupId::parse(FIXTURE_BACKUP_ID).expect("the fixture's id is a uuid")
}

/// The root every case writes into: the agent's own.
fn root() -> LocalBackupRoot {
    LocalBackupRoot::default()
}

/// The catalog the creation asks which databases the account owns.
fn catalog() -> DbHostCatalog<ProcessDbHost> {
    DbHostCatalog::new(Arc::new(ProcessDbHost::new(polygon_distro())))
}

/// A sink that records nothing and never interferes.
#[derive(Default)]
struct QuietSink;

impl ProgressSink for QuietSink {
    fn report(&mut self, _stage: BackupStage, _percent: u32) {}
}

impl RestoreSink for QuietSink {
    fn report(&mut self, _stage: RestoreStage, _percent: u32) {}
}

/// A sink that kills its own process the first time the restore reaches the
/// chosen moment.
///
/// `SIGKILL` and not `SIGTERM` or a panic: the whole proposition is a process
/// that stops existing with no unwinding, no `Drop`, and no chance to reverse
/// its own first rename. A panic would run destructors and would be a different
/// experiment; `SIGTERM` could be caught.
struct SuicideSink {
    /// Which moment to die at: [`KILL_IN_THE_SWAP`] or [`KILL_AFTER_A_DUMP`].
    at: String,
}

impl RestoreSink for SuicideSink {
    fn report(&mut self, stage: RestoreStage, percent: u32) {
        let in_the_swap = stage == RestoreStage::RestoringFiles
            && percent == RestoreStage::RestoringFiles.percent_through(1, 2);
        let after_a_dump = stage == RestoreStage::RestoringDatabases
            && percent > RestoreStage::RestoringDatabases.start_percent();

        let now = match self.at.as_str() {
            KILL_IN_THE_SWAP => in_the_swap,
            KILL_AFTER_A_DUMP => after_a_dump,
            _ => false,
        };

        if now {
            let _ = rustix::process::kill_process(
                rustix::process::getpid(),
                rustix::process::Signal::KILL,
            );
        }
    }
}

/// Refuses to go on unless this process is root inside a polygon image.
///
/// # Panics
///
/// Panics when the polygon marker is absent.
fn require_polygon() {
    let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
    assert!(
        !marker.is_empty(),
        "this suite kills a real restore of a real account and must run inside a \
         polygon container: {POLYGON_MARKER} is not set. See docker/README.md."
    );
}

/// The account directory a backup of `account` lands in.
fn account_directory(account: &AccountName) -> PathBuf {
    root().as_path().join(account.as_str())
}

/// Removes everything a previous run left for `account`.
fn clear_backups(account: &AccountName) {
    let _ = remove_dir_all(account_directory(account));
}

/// Removes anything a previous run left in the restore staging root.
///
/// Without this a case could pass on a marker an earlier case wrote, which would
/// make it a test of the previous run rather than of this one.
fn clear_staging_root() {
    let root = Path::new(AgentPaths::RESTORE_STAGING_ROOT);
    if let Ok(entries) = read_dir(root) {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_dir() {
                let _ = remove_dir_all(&path);
            } else {
                let _ = std::fs::remove_file(&path);
            }
        }
    }
}

/// Creates one real database for `account` and puts the backed-up row in it.
fn give_account_a_database(server: &PolygonMariadb, account: &AccountName) -> DatabaseName {
    let database = DatabaseName::for_account(account, "shop").expect("a valid database name");
    let name = database.as_str().to_owned();

    server.run(&format!("DROP DATABASE IF EXISTS `{name}`"));
    server.run(&format!("DROP USER IF EXISTS '{name}'@'localhost'"));

    create_database(
        &ProcessDbHost::new(polygon_distro()),
        &CreateDatabaseRequest {
            database: database.clone(),
            user: DbUserName::for_account(account, "shop").expect("a valid user name"),
            password: Password::parse(FIXTURE_PASSWORD).expect("a valid password"),
        },
    )
    .unwrap_or_else(|error| panic!("creating the fixture database must succeed: {error}"));

    server.run(&format!(
        "CREATE TABLE `{name}`.`orders` (note VARCHAR(64)); \
         INSERT INTO `{name}`.`orders` VALUES ('{NOTE_IN_THE_BACKUP}')"
    ));

    database
}

/// The single note the fixture table holds, as the real client reads it.
fn note_in(server: &PolygonMariadb, database: &DatabaseName) -> String {
    let output = server.run(&format!(
        "SELECT note FROM `{}`.`orders`",
        database.as_str()
    ));
    String::from_utf8_lossy(&output.stdout).trim().to_owned()
}

/// `<user>:<group>:<mode>` of `path`, as `stat` reports it.
fn stat_of(path: &Path) -> String {
    let output = Command::new("stat")
        .args(["-c", "%U:%G:%a"])
        .arg(path)
        .output()
        .expect("stat runs on a polygon host");
    String::from_utf8_lossy(&output.stdout).trim().to_owned()
}

/// Takes the backup every case restores from, and answers its digest.
///
/// The home holds [`BACKED_UP_BYTES`] and the database holds
/// [`NOTE_IN_THE_BACKUP`] at this moment; afterwards both are changed, so a
/// restore that did nothing is distinguishable from one that worked.
fn take_the_backup(server: &PolygonMariadb, account: &PolygonAccount) -> (DatabaseName, String) {
    clear_backups(account.name());
    clear_staging_root();
    let database = give_account_a_database(server, account.name());
    write(account.home().join("index.html"), BACKED_UP_BYTES).expect("the home's file");

    let summary = create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut QuietSink,
    )
    .unwrap_or_else(|error| panic!("the backup must succeed: {error}"));
    let digest = summary
        .readable_details()
        .expect("a completed creation describes itself")
        .artifact_sha256
        .clone();

    // Both halves moved on since, the way a customer's do.
    write(account.home().join("index.html"), LIVE_BYTES).expect("the mutation");
    server.run(&format!(
        "UPDATE `{}`.`orders` SET note = '{NOTE_BEFORE_THE_RESTORE}'",
        database.as_str()
    ));
    assert_eq!(note_in(server, &database), NOTE_BEFORE_THE_RESTORE);

    (database, digest)
}

/// Runs this same test binary again as the child half, and answers what
/// happened to it.
///
/// `--exact --ignored` and the case's own name, so there is no second artifact
/// to keep in step. The child sees [`CHILD_ACCOUNT`] and therefore takes the
/// restoring branch instead of the arranging one.
fn run_the_child(case: &str, account: &AccountName, digest: &str, kill_at: &str) -> Output {
    let polygon = std::env::var(POLYGON_MARKER).unwrap_or_default();

    Command::new(std::env::current_exe().expect("this test binary"))
        .args([
            case,
            "--exact",
            "--ignored",
            "--nocapture",
            "--test-threads=1",
        ])
        .env(POLYGON_MARKER, polygon)
        .env(CHILD_ACCOUNT, account.as_str())
        .env(CHILD_DIGEST, digest)
        .env(CHILD_KILL_AT, kill_at)
        .output()
        .expect("the child half of this case starts")
}

/// The child half: restore, and die where the parent asked.
///
/// Answers `true` when this process WAS the child, so the parent's half can
/// return without doing anything else.
fn restored_as_the_child() -> bool {
    let Ok(name) = std::env::var(CHILD_ACCOUNT) else {
        return false;
    };
    let account = AccountName::parse(&name).expect("the parent passes a valid name");
    let digest = std::env::var(CHILD_DIGEST).expect("the parent passes the digest");
    let at = std::env::var(CHILD_KILL_AT).expect("the parent passes the kill point");

    let database = DatabaseName::for_account(&account, "shop").expect("a valid database name");
    let group = GroupId::resolve(polygon_distro().web_server_group())
        .expect("the polygon has the web server's group")
        .gid();

    // Whatever this answers is irrelevant: the parent judges by the signal.
    let _ = restore_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &account,
        &fixture_id(),
        &root(),
        std::slice::from_ref(&database),
        &digest,
        group,
        &mut SuicideSink { at },
    );

    true
}

/// Asserts the child really was killed by signal 9.
///
/// A child that exited normally means the kill never happened, and every
/// assertion after it would be describing a restore that simply finished. That
/// is the blind spot this check closes.
fn assert_killed(outcome: &Output) {
    assert_eq!(
        std::os::unix::process::ExitStatusExt::signal(&outcome.status),
        Some(rustix::process::Signal::KILL.as_raw()),
        "the child half must die by SIGKILL, not exit: status {:?}, stderr {}",
        outcome.status,
        String::from_utf8_lossy(&outcome.stderr)
    );
}

/// The parked home for this account and this backup, as `AgentPaths` names it.
fn parked_home(account: &AccountName) -> PathBuf {
    AgentPaths::restore_previous_dir(account, &fixture_id())
}

/// The rollback directory a restore of this backup writes its pre-drop dumps
/// into.
fn rollback_directory() -> PathBuf {
    AgentPaths::backup_scratch_dir(&fixture_id()).join("rollback")
}

#[test]
#[ignore = "kills a real restore of a real account mid-swap: polygon only"]
fn a_restore_killed_between_its_two_renames_gives_the_home_back_at_the_next_start() {
    if restored_as_the_child() {
        return;
    }
    require_polygon();

    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polyrecovone");
    let (_database, digest) = take_the_backup(&server, &account);

    let outcome = run_the_child(
        "a_restore_killed_between_its_two_renames_gives_the_home_back_at_the_next_start",
        account.name(),
        &digest,
        KILL_IN_THE_SWAP,
    );
    assert_killed(&outcome);

    // THE DEFECT, observed. Before any recovery runs: the account has no home
    // and the customer's data is parked under a name only this agent's source
    // knows. A case that skipped this could pass against a restore that never
    // entered the window at all.
    assert!(
        !account.home().exists(),
        "the kill must have landed inside the swap window, leaving no home"
    );
    assert!(
        parked_home(account.name()).is_dir(),
        "the customer's home must be parked at {}",
        parked_home(account.name()).display()
    );

    // The next start of the daemon, exactly as `server::serve` runs it before
    // it binds the socket.
    let recovery = recover_restores();

    assert_eq!(
        recovery.swaps_completed, 1,
        "the interrupted swap must be the one thing this start finished"
    );
    assert_eq!(recovery.swaps_rolled_back, 0);

    // A VALUE, not an existence check: the backed-up bytes and not the ones the
    // customer had before the restore. Finishing forward is the promise, and a
    // reconciler that rolled back would put LIVE_BYTES here and still leave a
    // home that exists.
    assert_eq!(
        read_to_string(account.home().join("index.html")).expect("the home has its file"),
        BACKED_UP_BYTES
    );

    // Usable, and not merely present: the arrangement every site on the account
    // depends on — owned by the account, group-owned by the web server's group
    // so it can traverse, 0750. A home restored with the account's own group
    // serves a silent 403 from every vhost.
    assert_eq!(
        stat_of(account.home()),
        format!(
            "{}:{}:750",
            account.name().as_str(),
            polygon_distro().web_server_group()
        )
    );
    let restored = metadata(account.home().join("index.html")).expect("the restored file");
    assert_eq!(
        restored.uid(),
        account.ids().uid(),
        "the files inside the home are the account's own"
    );

    assert!(
        !parked_home(account.name()).exists(),
        "the parked tree is reclaimed once the swap is finished"
    );
}

#[test]
#[ignore = "kills a real restore of a real account after a rollback dump: polygon only"]
fn a_restore_killed_after_its_rollback_dumps_keeps_them_across_the_next_start() {
    if restored_as_the_child() {
        return;
    }
    require_polygon();

    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polyrecovtwo");
    let (database, digest) = take_the_backup(&server, &account);

    let outcome = run_the_child(
        "a_restore_killed_after_its_rollback_dumps_keeps_them_across_the_next_start",
        account.name(),
        &digest,
        KILL_AFTER_A_DUMP,
    );
    assert_killed(&outcome);

    let dump = rollback_directory().join(format!("{}.sql", database.as_str()));
    assert!(
        dump.is_file(),
        "the killed restore must have left its rollback dump at {}",
        dump.display()
    );

    // The pre-restore state of the database, and the ONLY copy of it. Asserted
    // by content, because a dump that exists and is empty rolls nothing back —
    // and because this exact string is what the unit file's
    // `ExecStartPre=-/bin/rm -rf /var/lib/maran-scratch` used to destroy on the
    // restart an operator performs in order to recover.
    let before = read_to_string(&dump).expect("the rollback dump is readable");
    assert!(
        before.contains(NOTE_BEFORE_THE_RESTORE),
        "the rollback dump must hold the row the restore dropped"
    );

    let recovery = recover_restores();

    assert_eq!(
        recovery.rollback_sets_kept, 1,
        "the start must keep the killed restore's rollback dumps"
    );
    assert_eq!(
        read_to_string(&dump).expect("the rollback dump survived the start"),
        before,
        "the pre-restore dump must be byte-for-byte what it was before the start"
    );

    // And the half that IS reproducible is gone, which is what the unit's line
    // existed for and what removing it must not have cost.
    assert!(
        !AgentPaths::backup_scratch_dir(&fixture_id())
            .join("databases")
            .exists(),
        "the archive's extracted dumps must not survive a start"
    );

    // The rollback material is usable: loading it puts the customer's row back.
    server.run(&format!("DROP DATABASE IF EXISTS `{}`", database.as_str()));
    server.run(&format!("CREATE DATABASE `{}`", database.as_str()));
    let loaded = Command::new(polygon_distro().mysql_client_binary())
        .args(["--protocol=socket", database.as_str()])
        .stdin(std::fs::File::open(&dump).expect("the dump opens"))
        .output()
        .expect("the client runs on a polygon host");
    assert!(
        loaded.status.success(),
        "the surviving rollback dump must load: {}",
        String::from_utf8_lossy(&loaded.stderr)
    );
    assert_eq!(note_in(&server, &database), NOTE_BEFORE_THE_RESTORE);

    let _ = remove_dir_all(AgentPaths::backup_scratch_dir(&fixture_id()));
}

#[test]
#[ignore = "runs a real restore of a real account to completion: polygon only"]
fn a_completed_restore_is_not_recovered_by_the_next_start() {
    if restored_as_the_child() {
        return;
    }
    require_polygon();

    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polyrecovthree");
    let (database, digest) = take_the_backup(&server, &account);

    let group = GroupId::resolve(polygon_distro().web_server_group())
        .expect("the polygon has the web server's group")
        .gid();
    restore_backup(
        &ProcessBackupHost::new(polygon_distro()),
        account.name(),
        &fixture_id(),
        &root(),
        std::slice::from_ref(&database),
        &digest,
        group,
        &mut QuietSink,
    )
    .unwrap_or_else(|error| panic!("the restore must succeed: {error}"));

    let before = read_to_string(account.home().join("index.html")).expect("the restored file");
    let ownership = stat_of(account.home());

    let recovery = recover_restores();

    // The tally is the assertion. "The home is still there" would pass just as
    // well against a reconciler that moved it away and put it back, and a
    // reconciler that fires on healthy state is worse than none.
    assert_eq!(recovery.swaps_completed, 0);
    assert_eq!(recovery.swaps_rolled_back, 0);
    assert_eq!(recovery.swaps_abandoned, 0);
    assert_eq!(recovery.swaps_already_done, 0);
    assert_eq!(recovery.refused, 0);
    assert_eq!(recovery.unmarked_left, 0);

    assert_eq!(
        read_to_string(account.home().join("index.html")).expect("the file is untouched"),
        before
    );
    assert_eq!(stat_of(account.home()), ownership);

    // A successful restore leaves the staging root with nothing of its own in
    // it, which is why the tally above is all zeroes rather than one
    // `swaps_already_done`.
    assert!(
        !parked_home(account.name()).exists(),
        "a successful restore removes its parked tree"
    );
}

#[test]
#[ignore = "restores a real account's home and database end to end: polygon only"]
fn a_normal_restore_still_puts_the_home_and_the_database_back() {
    if restored_as_the_child() {
        return;
    }
    require_polygon();

    // The positive control for everything above. The marker, the extra progress
    // report between the two renames and the startup reaping are all new on this
    // path; a suite that only proved the recovery works would not notice that
    // the ordinary restore had stopped working.
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polyrecovfour");
    let (database, digest) = take_the_backup(&server, &account);

    let group = GroupId::resolve(polygon_distro().web_server_group())
        .expect("the polygon has the web server's group")
        .gid();
    let outcome = restore_backup(
        &ProcessBackupHost::new(polygon_distro()),
        account.name(),
        &fixture_id(),
        &root(),
        std::slice::from_ref(&database),
        &digest,
        group,
        &mut QuietSink,
    )
    .unwrap_or_else(|error| panic!("the restore must succeed: {error}"));

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, outcome.databases_total);
    assert_eq!(outcome.databases_total, 1);

    assert_eq!(
        read_to_string(account.home().join("index.html")).expect("the file is back"),
        BACKED_UP_BYTES
    );
    assert_eq!(note_in(&server, &database), NOTE_IN_THE_BACKUP);
    assert_eq!(
        stat_of(account.home()),
        format!(
            "{}:{}:750",
            account.name().as_str(),
            polygon_distro().web_server_group()
        )
    );

    // The marker's whole lifecycle, observed at its end: a successful restore
    // leaves none behind, so the next start has nothing to reconcile. A marker
    // that outlived its restore would make every start read a document about an
    // account whose home is fine.
    let leftovers: Vec<String> = read_dir(AgentPaths::RESTORE_STAGING_ROOT)
        .map(|entries| {
            entries
                .flatten()
                .map(|entry| entry.file_name().to_string_lossy().into_owned())
                .collect()
        })
        .unwrap_or_default();
    assert!(
        leftovers.is_empty(),
        "a successful restore leaves the staging root empty, found: {leftovers:?}"
    );

    // And the scratch, whose removal is the operation's own and not the
    // startup's.
    assert!(!AgentPaths::backup_scratch_dir(&fixture_id()).exists());

    // Not part of the proposition, but the suite must not depend on a directory
    // it never created existing.
    let _ = create_dir_all(AgentPaths::RESTORE_STAGING_ROOT);
}
