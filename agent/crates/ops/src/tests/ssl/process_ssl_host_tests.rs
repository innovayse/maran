//! What the real host does to real files.
//!
//! Every other test in this area drives a fake that stores a `String` in a map,
//! which can say nothing about the thing the design actually rests on: the mode
//! of a private key on disk. These tests use real directories and read the real
//! `st_mode` back. They need no root — a temporary directory and `chmod` are
//! enough to pin the behaviour that matters.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;

use crate::ssl::process_ssl_host::create_private_directory;

/// The mode of `path`, masked to the permission bits.
fn mode_of(path: &Path) -> u32 {
    std::fs::metadata(path).unwrap().permissions().mode() & 0o777
}

#[test]
fn the_material_directory_is_created_traversable_by_root_alone() {
    let root = tempfile::tempdir().unwrap();
    let store = root.path().join("certificates").join("example.com");

    create_private_directory(&store).unwrap();

    // Not `0o777 & !umask`, which is what `create_dir_all` alone gives and which
    // therefore depends on how the daemon happened to be started. A private key
    // lives in here.
    assert_eq!(mode_of(&store), 0o700);
}

#[test]
fn a_directory_that_already_exists_at_a_wider_mode_is_narrowed() {
    let root = tempfile::tempdir().unwrap();
    let store = root.path().join("certificates");
    std::fs::create_dir_all(&store).unwrap();
    std::fs::set_permissions(&store, std::fs::Permissions::from_mode(0o755)).unwrap();

    create_private_directory(&store).unwrap();

    // The mode is re-asserted on every call rather than only on creation: a
    // store left at 0755 by an older agent, a restore from a backup, or an
    // operator is corrected rather than inherited — and 0755 here is every
    // account on the host being able to list the certificate directory.
    assert_eq!(mode_of(&store), 0o700);
}

#[test]
fn creating_the_directory_twice_is_a_success_that_changes_nothing() {
    let root = tempfile::tempdir().unwrap();
    let store = root.path().join("certificates");

    create_private_directory(&store).unwrap();
    create_private_directory(&store).unwrap();

    assert_eq!(mode_of(&store), 0o700);
}
