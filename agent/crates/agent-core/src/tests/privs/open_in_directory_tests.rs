//! Tests for [`open_in_directory`].
//!
//! The `unsafe` in this workspace is small enough to read and dangerous enough
//! to test, and none of this needs root: the name checks are pure logic, and
//! the rest runs against a temporary directory the test itself owns.
//!
//! What is pinned is the two properties the log tail rests on — that a name
//! cannot leave the directory the descriptor pins, and that "there is no file"
//! is distinguishable from "the open was refused". The caller treats the first
//! as an empty log and the second as an attack, so collapsing them would
//! silently render a symlink attempt as a site with no traffic.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::unix::ffi::OsStrExt;
use std::os::unix::fs::{OpenOptionsExt, symlink};

use super::open_in_directory;

/// The flags the log tail uses, so the tests exercise the real call shape.
const FLAGS: libc::c_int = libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK;

/// Opens `directory` the way the tail does, as a pinned descriptor.
fn pin(directory: &std::path::Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(directory)
        .unwrap()
}

#[test]
fn a_name_that_would_walk_out_of_the_directory_is_refused_before_the_syscall() {
    let directory = tempfile::tempdir().unwrap();
    let pinned = pin(directory.path());

    // The one that matters: `openat` resolves a relative path, so this would
    // otherwise leave the pinned directory and undo the whole design.
    for name in [
        "../../../etc/shadow",
        "..",
        ".",
        "sub/file",
        "/etc/shadow",
        "",
    ] {
        let outcome = open_in_directory(&pinned, OsStr::new(name), FLAGS);
        let error = outcome
            .err()
            .unwrap_or_else(|| panic!("{name} must be refused"));
        assert_eq!(
            error.kind(),
            io::ErrorKind::InvalidInput,
            "{name} must be refused as a bad name, not passed to the kernel"
        );
    }
}

#[test]
fn an_embedded_nul_is_refused_rather_than_truncated() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join("access.log"), "line\n").unwrap();
    let pinned = pin(directory.path());

    // Truncating at the NUL would open `access.log` while every human reading
    // the name saw something else. Refusal is the only safe answer.
    let name = OsStr::from_bytes(b"access.log\0/../../etc/shadow");
    let error = open_in_directory(&pinned, name, FLAGS).err().unwrap();

    assert_eq!(error.kind(), io::ErrorKind::InvalidInput);
}

#[test]
fn a_symlink_is_refused_and_is_not_reported_as_a_missing_file() {
    let directory = tempfile::tempdir().unwrap();
    let elsewhere = directory.path().join("secret");
    std::fs::write(&elsewhere, "not yours\n").unwrap();
    symlink(&elsewhere, directory.path().join("access.log")).unwrap();

    let pinned = pin(directory.path());
    let error = open_in_directory(&pinned, OsStr::new("access.log"), FLAGS)
        .expect_err("O_NOFOLLOW must refuse a symlink");

    // The distinction the caller depends on: NotFound means "no traffic yet",
    // anything else means somebody is trying something.
    assert_ne!(
        error.kind(),
        io::ErrorKind::NotFound,
        "a symlink must not be reported as an absent log"
    );
}

#[test]
fn a_missing_file_is_not_found_and_a_real_one_opens() {
    let directory = tempfile::tempdir().unwrap();
    let pinned = pin(directory.path());

    let missing = open_in_directory(&pinned, OsStr::new("access.log"), FLAGS)
        .err()
        .unwrap();
    assert_eq!(missing.kind(), io::ErrorKind::NotFound);

    std::fs::write(directory.path().join("access.log"), "line\n").unwrap();
    open_in_directory(&pinned, OsStr::new("access.log"), FLAGS)
        .expect("a plain regular file must open");
}

#[test]
fn the_descriptor_pins_the_directory_a_rename_cannot_move() {
    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("logs");
    std::fs::create_dir(&logs).unwrap();
    std::fs::write(logs.join("access.log"), "real\n").unwrap();

    let pinned = pin(&logs);

    // The attack the whole design exists for: swap the directory out from under
    // a long-running tail and point its name somewhere else.
    let decoy = root.path().join("decoy");
    std::fs::create_dir(&decoy).unwrap();
    std::fs::write(decoy.join("access.log"), "planted\n").unwrap();
    std::fs::rename(&logs, root.path().join("logs.old")).unwrap();
    symlink(&decoy, &logs).unwrap();

    let mut opened = open_in_directory(&pinned, OsStr::new("access.log"), FLAGS).unwrap();
    let mut content = String::new();
    std::io::Read::read_to_string(&mut opened, &mut content).unwrap();

    assert_eq!(
        content, "real\n",
        "the descriptor names an inode, so the swapped path must not be consulted"
    );
}
