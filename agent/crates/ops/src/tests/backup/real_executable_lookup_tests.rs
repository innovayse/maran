//! The real filesystem lookup, exercised against a real temporary file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, set_permissions, write};

use tempfile::TempDir;

use super::*;

/// A path with no execute bit is reported not executable, even though it
/// exists and is a regular file — the distinction the check actually needs,
/// since `tar`, `gzip` and the dump client are all spawned, not merely read.
#[test]
fn a_regular_file_with_no_execute_bit_is_not_executable() {
    let directory = TempDir::new().expect("a temporary directory");
    let path = directory.path().join("not-a-program");
    write(&path, b"#!/bin/sh\n").expect("the fixture file is writable");
    set_permissions(&path, Permissions::from_mode(0o644)).expect("the fixture is chmodable");

    assert!(!RealExecutableLookup.is_executable(path.to_str().expect("utf8 path")));
}

/// A regular file with an execute bit set is reported executable.
#[test]
fn a_regular_file_with_an_execute_bit_is_executable() {
    let directory = TempDir::new().expect("a temporary directory");
    let path = directory.path().join("a-program");
    write(&path, b"#!/bin/sh\n").expect("the fixture file is writable");
    set_permissions(&path, Permissions::from_mode(0o755)).expect("the fixture is chmodable");

    assert!(RealExecutableLookup.is_executable(path.to_str().expect("utf8 path")));
}

/// A path naming nothing at all is reported not executable, rather than the
/// lookup propagating the `stat` failure — the caller only needs one bit.
#[test]
fn a_path_that_names_nothing_is_not_executable() {
    let directory = TempDir::new().expect("a temporary directory");
    let path = directory.path().join("does-not-exist");

    assert!(!RealExecutableLookup.is_executable(path.to_str().expect("utf8 path")));
}

/// A directory is not executable, however its bits are set — the check is for
/// programs the agent spawns by path, and `execve` on a directory fails.
#[test]
fn a_directory_is_not_executable_even_with_the_execute_bit_set() {
    let directory = TempDir::new().expect("a temporary directory");

    assert!(!RealExecutableLookup.is_executable(directory.path().to_str().expect("utf8 path")));
}
