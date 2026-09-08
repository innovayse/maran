//! Tests for [`available_bytes`].
//!
//! PARTLY RECONSTRUCTED 2026-09-07. The original 88-line file was deleted in
//! error by a session that mistook this uncommitted work for landed code, and
//! could not be recovered — it had never been committed. What the original
//! covered is known from its own author's write-up
//! (`.superpowers/sdd/2026-09-05-maran-backups/scratch-ceiling-report.md` §5):
//! three running tests (a real directory answers with room; a missing path is
//! an `Err` and never a generous default; the answer is the path's OWN mount
//! and not a parent's) plus one `#[ignore]`d `df` control, which was the only
//! thing that killed the `f_bavail -> f_bfree` and `f_frsize -> 1` mutations.
//! The text below is not the original author's; the claims it pins are.
//!
//! The `df` control does NOT live here. An `#[ignore]`d unit test is in no
//! automated lane at all — `maran test rust` skips it because it is ignored,
//! and the polygon lane never sees it because `polygon_suites` discovers
//! crate-level `tests/*.rs` files, not unit modules — so it ran only when
//! somebody typed its name. It is now
//! `the_root_filesystem_reports_the_bytes_df_says_an_unprivileged_writer_could_add`
//! in `agent/crates/agent/tests/backup_on_a_real_host.rs`, the suite for the
//! feature this helper bounds, which CI runs on both families.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use crate::utils::available_bytes::available_bytes;

/// How far this helper's answer may differ from `df`'s before the two are
/// answering different questions.
///
/// One percent. They read the same `statvfs` on the same filesystem, so the
/// tolerance covers a write landing between the two readings and nothing else.
/// It is deliberately far tighter than the root reserve that separates
/// `f_bfree` from `f_bavail` — typically 5% — because that difference is
/// exactly what the `df` control in the backup polygon suite exists to catch.
const DF_TOLERANCE: f64 = 0.01;

/// Directories that are a separate mount from `/` on a great many hosts.
///
/// The mount test needs two directories on two filesystems and cannot create a
/// mount itself without privileges it does not have in a unit run.
const CANDIDATE_MOUNTS: [&str; 3] = ["/dev/shm", "/run", "/sys/fs/cgroup"];

/// Whether two readings of the same filesystem agree.
///
/// Not equality. Two `statvfs` calls a few microseconds apart are two separate
/// measurements, and anything else writing to that filesystem in between moves
/// the second — during a full test run, something always is. The tolerance is
/// the same one the monitor area's `df` control uses, and it is far tighter
/// than the root reserve (typically 5%) or a block size, which are the two
/// differences these assertions exist to catch.
fn agrees(reading: u64, expected: u64) -> bool {
    reading.abs_diff(expected) as f64 <= expected as f64 * DF_TOLERANCE
}

#[test]
fn an_existing_directory_reports_a_figure_for_the_filesystem_it_sits_on() {
    let directory = tempfile::tempdir().unwrap();

    let answer = available_bytes(directory.path()).unwrap();

    // The inverse control for the refusing assertions below: a helper that
    // answered `Err` to everything would satisfy them all and be useless.
    assert!(
        answer > 0,
        "an existing directory must report room: {answer}"
    );
}

#[test]
fn a_path_that_does_not_exist_is_an_error_rather_than_a_generous_default() {
    // The direction matters. A ceiling that falls back to a large number when
    // it could not measure is a ceiling that disappears exactly when the
    // filesystem is unhealthy, which is when it is the only thing standing
    // between a bulk write and a full disk.
    let missing = Path::new("/nonexistent-maran-available-bytes-probe");

    assert!(available_bytes(missing).is_err());
}

#[test]
fn a_file_where_a_directory_was_expected_is_an_error_and_not_its_parents_figure() {
    // `statvfs` answers happily for a regular file, reporting the filesystem
    // that file sits on — so this is NOT a test that the syscall refuses. It
    // pins the one thing a caller must never be able to do by accident: get a
    // number back for a path component that is not a directory at all. A dump
    // staged "into" a file path is a caller that has not created its scratch,
    // and the answer it deserves is a refusal, not a figure.
    let directory = tempfile::tempdir().unwrap();
    let file = directory.path().join("not-a-directory");
    std::fs::write(&file, b"x").unwrap();

    let inside_the_file = file.join("dump.sql");

    assert!(available_bytes(&inside_the_file).is_err());
}

#[test]
fn the_answer_is_the_directorys_own_mount_and_never_its_parents() {
    // This is the claim the helper exists for. `/run/maran/scratch` and
    // `/var/lib/maran-scratch` differ by two orders of magnitude, and a
    // ceiling computed against the wrong mount reports on a filesystem nothing
    // is being written to.
    let root_device = std::fs::metadata("/").unwrap().dev();
    let separate = CANDIDATE_MOUNTS.iter().map(Path::new).find(|candidate| {
        std::fs::metadata(candidate)
            .map(|entry| entry.dev() != root_device)
            .unwrap_or(false)
    });

    let Some(separate) = separate else {
        // Stated rather than hidden, per rules/testing.md: a check that cannot
        // observe its subject must say so in its own output instead of passing
        // quietly.
        eprintln!(
            "UNOBSERVED HERE: none of {CANDIDATE_MOUNTS:?} is a separate mount from / on this \
             host, so the parent-versus-mount claim could not be exercised"
        );
        return;
    };

    let mounted_figure = available_bytes(separate).unwrap();
    let parent = separate.parent().unwrap();
    let parent_figure = available_bytes(parent).unwrap();

    // The two directories are one path component apart and on two
    // filesystems. A helper that measured the parent would return the parent's
    // number for both.
    assert_ne!(
        mounted_figure,
        parent_figure,
        "{} and its parent {} are different filesystems and must not report the same figure",
        separate.display(),
        parent.display()
    );
    let space = rustix::fs::statvfs(separate).unwrap();
    let expected = space.f_bavail * space.f_frsize;
    assert!(
        agrees(mounted_figure, expected),
        "the figure must be that mount's own: {mounted_figure} against {expected}"
    );
}

#[test]
fn the_figure_is_what_an_unprivileged_writer_could_add_and_not_the_free_count() {
    // `statvfs` answers with three block counts and only one of them is a
    // promise to a non-root writer. `f_bfree` includes the reserve ext4 and
    // friends keep for root, and `f_blocks` is the whole filesystem.
    //
    // Blind spot, stated: on a filesystem with NO reserve the three counts
    // coincide, and on `/tmp` they very often do — so this test cannot by
    // itself separate `f_bavail` from `f_bfree`. Nothing in this file can:
    // the `df` control that does lives in the backup polygon suite, where a
    // lane actually runs it.
    let directory = tempfile::tempdir().unwrap();
    let space = rustix::fs::statvfs(directory.path()).unwrap();

    let answer = available_bytes(directory.path()).unwrap();

    let expected = space.f_bavail * space.f_frsize;
    assert!(
        agrees(answer, expected),
        "the figure must be the available count: {answer} against {expected}"
    );
    assert!(answer <= space.f_bfree * space.f_frsize);
    assert!(answer <= space.f_blocks * space.f_frsize);
}
