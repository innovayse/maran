//! Tests for the discipline a site log is opened with, against a real
//! directory.
//!
//! None of them needs root: a test owns a `tempfile::TempDir`, passes its own
//! uid as the one to trust, and the refusals are the same inode checks the root
//! daemon applies.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::os::unix::fs::symlink;

use maran_agent_core::utils::current_uid::current_uid;

use super::open_site_log;
use crate::sites::SitesOpError;
use crate::sites::open_site_log_directory::open_site_log_directory;

const LOG: &str = "a.com.access.log";

fn opened(directory: &std::path::Path, uid: u32) -> Result<bool, SitesOpError> {
    let held = open_site_log_directory(directory, current_uid().unwrap())?;
    open_site_log(&held, OsStr::new(LOG), uid).map(|found| found.is_some())
}

fn assert_refused(outcome: Result<bool, SitesOpError>, what: &str) {
    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("{what} must be refused as unreadable, got {other:?}"),
    }
}

#[test]
fn a_regular_file_the_trusted_uid_owns_is_opened() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    assert!(opened(directory.path(), current_uid().unwrap()).unwrap());
}

#[test]
fn a_log_that_does_not_exist_yet_is_not_an_error() {
    let directory = tempfile::tempdir().unwrap();

    assert!(!opened(directory.path(), current_uid().unwrap()).unwrap());
}

#[test]
fn a_symlink_in_place_of_the_log_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("secret"), "x").unwrap();
    symlink(outside.path().join("secret"), directory.path().join(LOG)).unwrap();

    assert_refused(
        opened(directory.path(), current_uid().unwrap()),
        "a symlink",
    );
}

#[test]
fn a_hard_link_in_place_of_the_log_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("secret"), "x").unwrap();
    std::fs::hard_link(outside.path().join("secret"), directory.path().join(LOG)).unwrap();

    assert_refused(
        opened(directory.path(), current_uid().unwrap()),
        "a hard link",
    );
}

#[test]
fn a_fifo_in_place_of_the_log_is_refused_and_does_not_hang() {
    let directory = tempfile::tempdir().unwrap();
    // Spawned rather than called through libc: this crate forbids unsafe code.
    let made = std::process::Command::new("mkfifo")
        .arg(directory.path().join(LOG))
        .status()
        .expect("mkfifo must be available");
    assert!(made.success());

    assert_refused(opened(directory.path(), current_uid().unwrap()), "a FIFO");
}

#[test]
fn a_directory_in_place_of_the_log_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::create_dir(directory.path().join(LOG)).unwrap();

    assert_refused(
        opened(directory.path(), current_uid().unwrap()),
        "a directory",
    );
}

#[test]
fn a_file_another_uid_owns_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    assert_refused(
        opened(directory.path(), current_uid().unwrap().wrapping_add(1)),
        "a file the trusted uid does not own",
    );
}

#[test]
fn a_log_directory_another_uid_owns_is_refused() {
    let directory = tempfile::tempdir().unwrap();

    assert_refused(
        open_site_log_directory(directory.path(), current_uid().unwrap().wrapping_add(1))
            .map(|_| true),
        "a directory the trusted uid does not own",
    );
}

#[test]
fn a_log_directory_that_is_a_symlink_is_refused() {
    let real = tempfile::tempdir().unwrap();
    let holder = tempfile::tempdir().unwrap();
    let link = holder.path().join("sites");
    symlink(real.path(), &link).unwrap();

    assert_refused(
        open_site_log_directory(&link, current_uid().unwrap()).map(|_| true),
        "a symlinked directory",
    );
}
