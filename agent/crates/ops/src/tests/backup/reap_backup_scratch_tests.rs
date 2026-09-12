//! What survives a start, and what bounds how much of it there can be.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir_all, read_to_string, write};
use std::path::{Path, PathBuf};

use tempfile::TempDir;

use super::*;

/// A bulk scratch root laid out as the operations leave it.
struct Scratch {
    /// The root, removed when the fixture drops.
    root: TempDir,
}

impl Scratch {
    /// An empty root with the `backup` area in it.
    fn new() -> Self {
        let root = TempDir::new().expect("a temporary directory");
        create_dir_all(root.path().join(SCRATCH_AREA)).expect("the backup area");
        Self { root }
    }

    /// The root itself.
    fn path(&self) -> &Path {
        self.root.path()
    }

    /// One operation's scratch: an extracted archive dump, and optionally a
    /// rollback dump of the given database.
    fn operation(&self, id: &str, rollback: Option<&str>) -> PathBuf {
        let scratch = self.root.path().join(SCRATCH_AREA).join(id);
        create_dir_all(scratch.join("databases")).expect("the extracted dumps");
        write(
            scratch.join("databases/shop.sql"),
            "SELECT 'from the archive';\n",
        )
        .expect("an extracted dump");

        if let Some(database) = rollback {
            create_dir_all(scratch.join(ROLLBACK_DIRECTORY)).expect("the rollback directory");
            write(
                scratch
                    .join(ROLLBACK_DIRECTORY)
                    .join(format!("{database}.sql")),
                "SELECT 'before the restore';\n",
            )
            .expect("a rollback dump");
        }
        scratch
    }

    /// Reaps, and answers the tally.
    fn reap(&self) -> RestoreRecovery {
        let mut recovery = RestoreRecovery::default();
        reap_backup_scratch(self.root.path(), &mut recovery);
        recovery
    }
}

#[test]
fn the_rollback_dumps_of_a_killed_restore_survive_the_start_that_reaps_the_scratch() {
    let scratch = Scratch::new();
    let operation = scratch.operation("3f2504e0-4f89-41d3-9a0c-0305e82c3301", Some("shop"));

    let recovery = scratch.reap();

    // The whole point of the change: the file whose deletion was the data loss.
    // Asserted by its CONTENT, not by its existence — a zero-length file left
    // behind would satisfy `exists()` and roll nothing back.
    assert_eq!(
        read_to_string(operation.join(ROLLBACK_DIRECTORY).join("shop.sql"))
            .expect("the rollback dump is still there"),
        "SELECT 'before the restore';\n"
    );
    assert_eq!(recovery.rollback_sets_kept, 1);
}

#[test]
fn the_archives_own_extracted_dumps_are_removed_because_a_retry_reproduces_them() {
    let scratch = Scratch::new();
    let operation = scratch.operation("3f2504e0-4f89-41d3-9a0c-0305e82c3301", Some("shop"));

    let recovery = scratch.reap();

    // This is the half the unit's `rm -rf` existed for, and it is still done on
    // every start: it is the bulk, and it is reproducible from the artifact.
    assert!(
        !operation.join("databases").exists(),
        "the extracted archive dumps must not survive a start"
    );
    assert!(recovery.scratch_entries_removed >= 1);
}

#[test]
fn a_scratch_with_no_rollback_dumps_is_removed_entirely() {
    let scratch = Scratch::new();
    let operation = scratch.operation("3f2504e0-4f89-41d3-9a0c-0305e82c3301", None);

    let recovery = scratch.reap();

    assert!(!operation.exists());
    assert_eq!(recovery.rollback_sets_kept, 0);
}

#[test]
fn an_empty_rollback_directory_does_not_occupy_a_retention_slot() {
    let scratch = Scratch::new();
    let operation = scratch.operation("3f2504e0-4f89-41d3-9a0c-0305e82c3301", None);
    create_dir_all(operation.join(ROLLBACK_DIRECTORY)).expect("an empty rollback directory");

    let recovery = scratch.reap();

    assert!(!operation.exists());
    assert_eq!(recovery.rollback_sets_kept, 0);
}

#[test]
fn anything_directly_under_the_root_that_is_not_the_backup_area_is_removed() {
    let scratch = Scratch::new();
    let stray = scratch.path().join("left-by-something-else");
    create_dir_all(&stray).expect("the stray directory");
    let stray_file = scratch.path().join("stray.sql");
    write(&stray_file, "SELECT 1;\n").expect("the stray file");

    scratch.reap();

    assert!(!stray.exists());
    assert!(!stray_file.exists());
}

#[test]
fn the_number_of_surviving_rollback_sets_is_bounded_and_the_oldest_go_first() {
    let scratch = Scratch::new();
    let mut operations = Vec::new();
    for index in 0..RETAINED_ROLLBACK_SETS + 2 {
        let operation = scratch.operation(
            &format!("3f2504e0-4f89-41d3-9a0c-0305e82c33{index:02}"),
            Some("shop"),
        );
        // Ordered by modification time and NOT by name, because the retention is
        // ordered by time. Set explicitly rather than relying on the clock's
        // resolution: two directories created in one microsecond would otherwise
        // sort arbitrarily and this assertion would be about nothing.
        let stamp = filetime_seconds(1_700_000_000 + index as i64);
        set_directory_time(&operation, stamp);
        operations.push(operation);
    }

    let recovery = scratch.reap();

    assert_eq!(recovery.rollback_sets_kept as usize, RETAINED_ROLLBACK_SETS);
    assert!(
        !operations[0].join(ROLLBACK_DIRECTORY).exists(),
        "the oldest rollback set must be the one that goes"
    );
    assert!(
        operations[operations.len() - 1]
            .join(ROLLBACK_DIRECTORY)
            .join("shop.sql")
            .exists(),
        "the newest rollback set must survive"
    );
}

#[test]
fn the_kept_directory_is_the_one_the_restore_writes_its_rollback_dumps_into() {
    // The two constants are deliberately not shared: this reaper is the one
    // thing in the system whose job is to NOT delete that directory, and
    // importing the name from the module that creates it would let a rename turn
    // this reaper silently back into `rm -rf`. So they are compared instead.
    assert_eq!(ROLLBACK_DIRECTORY, "rollback");

    // And the agreement made observable rather than asserted: a restore really
    // writes its rollback dumps into a directory of this name beside the
    // extracted ones, so a scratch built by the fixture the way the operation
    // builds it is one this reaper keeps.
    let scratch = Scratch::new();
    let operation = scratch.operation("3f2504e0-4f89-41d3-9a0c-0305e82c3301", Some("shop"));
    scratch.reap();

    assert!(operation.join(ROLLBACK_DIRECTORY).join("shop.sql").exists());
}

/// A `SystemTime` `seconds` after the epoch.
fn filetime_seconds(seconds: i64) -> std::time::SystemTime {
    std::time::UNIX_EPOCH + std::time::Duration::from_secs(seconds.unsigned_abs())
}

/// Sets `path`'s modification time, which is what the retention orders by.
fn set_directory_time(path: &Path, time: std::time::SystemTime) {
    let times = rustix::fs::Timestamps {
        last_access: to_timespec(time),
        last_modification: to_timespec(time),
    };
    rustix::fs::utimensat(rustix::fs::CWD, path, &times, rustix::fs::AtFlags::empty())
        .expect("the fixture can set its own directory's time");
}

/// The rustix timespec for a `SystemTime` after the epoch.
fn to_timespec(time: std::time::SystemTime) -> rustix::fs::Timespec {
    let since = time
        .duration_since(std::time::UNIX_EPOCH)
        .expect("the fixture's times are after the epoch");
    rustix::fs::Timespec {
        tv_sec: since.as_secs() as i64,
        tv_nsec: since.subsec_nanos() as i64,
    }
}
