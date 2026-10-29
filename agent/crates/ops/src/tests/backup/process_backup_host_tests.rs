//! What the real backup host makes of the programs it runs.
//!
//! The host can only be handed an adapter's binaries, so the root-side
//! archiver body (`run_archiver`, which takes a `&Path`) is driven with
//! stand-in programs, and the listing is driven with the real `tar` and
//! `gzip` the Debian adapter names. The calls that run for hours cannot be
//! timed out for real here; their mapping is `bound_or`, tested beside it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_distro::{DistroFamily, adapter_for};

use super::*;

/// The program that materialises a stand-in in a process of its own.
///
/// A platform literal, which tests may name. The stand-in is copied into place
/// by another process so that no descriptor in this one ever points at it for
/// writing: a fork in any other test thread would otherwise carry that
/// descriptor into a child and the exec would be refused with `ETXTBSY`.
const COPY_PROGRAM: &str = "/bin/cp";

/// Writes an executable stand-in whose body is `body` into `directory` and
/// returns its path.
fn stand_in(directory: &Path, body: &str) -> PathBuf {
    let source = directory.join("archiver.source");
    let program = directory.join("archiver");

    std::fs::write(&source, format!("#!/bin/sh\n{body}")).unwrap();
    let copied = Command::new(COPY_PROGRAM)
        .arg(&source)
        .arg(&program)
        .status()
        .unwrap();
    assert!(
        copied.success(),
        "the stand-in could not be copied into place"
    );
    std::fs::set_permissions(&program, std::fs::Permissions::from_mode(0o700)).unwrap();

    program
}

/// Whether the process `pid` is gone, a zombie counting as gone.
fn is_gone(pid: u32) -> bool {
    match std::fs::read_to_string(format!("/proc/{pid}/stat")) {
        Err(_) => true,
        Ok(stat) => stat
            .rsplit(')')
            .next()
            .is_some_and(|rest| rest.trim_start().starts_with('Z')),
    }
}

/// Polls `condition` for up to `limit`, answering whether it became true.
fn eventually(limit: Duration, condition: impl Fn() -> bool) -> bool {
    let start = Instant::now();
    while start.elapsed() < limit {
        if condition() {
            return true;
        }
        std::thread::sleep(Duration::from_millis(20));
    }

    condition()
}

#[test]
fn an_archiver_that_exits_cleanly_is_a_success() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in(root.path(), "exit 0\n");

    let outcome = run_archiver(&program, &[], Stdio::null(), Duration::from_secs(30));

    assert_eq!(outcome, Ok(()));
}

#[test]
fn an_archiver_that_exits_non_zero_reports_its_status() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in(root.path(), "exit 3\n");

    let outcome = run_archiver(&program, &[], Stdio::null(), Duration::from_secs(30));

    assert_eq!(outcome, Err(BackupError::ArchiveFailed { status: 3 }));
}

#[test]
fn an_archiver_that_cannot_be_started_is_program_unavailable_and_not_a_bound() {
    let root = tempfile::tempdir().unwrap();

    let outcome = run_archiver(
        &root.path().join("absent"),
        &[],
        Stdio::null(),
        Duration::from_secs(30),
    );

    assert_eq!(
        outcome,
        Err(BackupError::ArchiveFailed {
            status: PROGRAM_UNAVAILABLE
        })
    );
}

/// The archiver is killed at its deadline together with its process group, and
/// the failure is the typed bound. The grandchild stands for the compressor
/// `tar` starts, which a kill of the leader's pid alone would orphan.
#[test]
fn an_archiver_past_its_deadline_is_a_bound_and_its_group_is_killed() {
    let root = tempfile::tempdir().unwrap();
    let pid_file = root.path().join("grandchild.pid");
    let program = stand_in(
        root.path(),
        &format!("sleep 300 &\necho $! > {}\nwait\n", pid_file.display()),
    );

    let started = Instant::now();
    let outcome = run_archiver(&program, &[], Stdio::null(), Duration::from_millis(1500));

    assert!(
        matches!(
            outcome,
            Err(BackupError::Bound(BoundExceeded::TimedOut { .. }))
        ),
        "expected the typed timeout, got {outcome:?}"
    );
    assert!(
        started.elapsed() < Duration::from_secs(30),
        "the kill did not fire at the deadline"
    );
    let pid: u32 = std::fs::read_to_string(&pid_file)
        .unwrap()
        .trim()
        .parse()
        .unwrap();
    assert!(
        eventually(Duration::from_secs(10), || is_gone(pid)),
        "the compressor stand-in outlived its archiver's group"
    );
}

/// The account-side deadline plus the core's reap ceiling must fit inside the
/// 120 s `fork_as_account` allows its child, so the inner kill fires first and
/// takes the archiver and its compressor down together.
///
/// The 120 is a literal: the constant is private in `agent-core::privs`
/// (`CHILD_PATIENCE`). The 5 is the core's reap ceiling, private to it too.
#[test]
fn the_account_side_archiver_deadline_leaves_room_for_the_reap_below_child_patience() {
    let deadline = std::hint::black_box(ACCOUNT_ARCHIVER_DEADLINE);

    assert!(deadline + Duration::from_secs(5) < Duration::from_secs(120));
}

/// The host lists a real archive through the core, in the order `tar` prints.
#[test]
fn the_host_lists_a_real_archive_through_the_bounded_core() {
    let root = tempfile::tempdir().unwrap();
    let source = root.path().join("source");
    std::fs::create_dir(&source).unwrap();
    std::fs::write(source.join("a.txt"), "a\n").unwrap();
    let artifact = root.path().join("artifact.tar.gz");
    let made = Command::new("/usr/bin/tar")
        .args(["--create", "--gzip", "--file"])
        .arg(&artifact)
        .arg("--directory")
        .arg(&source)
        .arg("a.txt")
        .status()
        .unwrap();
    assert!(made.success());
    let host = ProcessBackupHost::new(adapter_for(DistroFamily::Debian));

    let members = host.list_members(&artifact).unwrap();

    assert_eq!(members, vec!["a.txt"]);
}

/// Every one of the host's five spawns (archive, dump, extract, drop, load) is
/// a job that may run for hours, so each runs under the bulk deadline. A
/// shorter class at any of them kills legitimate work, and no other test waits
/// long enough to notice.
#[test]
fn every_spawn_of_the_backup_host_runs_under_the_bulk_class() {
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../backup/process_backup_host.rs"
    ));

    assert_eq!(classes, ["Bulk"; 5]);
}
