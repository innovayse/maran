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

use maran_agent_core::command_outcome::CommandOutcome;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError};
use crate::ssl::process_ssl_host::{create_private_directory, reassert_mode, remove_one};
use crate::ssl::ssl_op_error::SslOpError;

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

#[test]
fn a_key_the_rollback_put_back_is_narrowed_to_exactly_0600() {
    let root = tempfile::tempdir().unwrap();
    let key = root.path().join("privkey.pem");

    // Exactly what `RollbackGuard::restore` does after a removal: the file was
    // unlinked, so the write CREATES it and the mode is the umask's rather
    // than the 0600 the key was written at.
    std::fs::write(&key, "-----BEGIN PRIVATE KEY-----\n").unwrap();
    std::fs::set_permissions(&key, std::fs::Permissions::from_mode(0o600)).unwrap();
    std::fs::remove_file(&key).unwrap();
    std::fs::write(&key, "-----BEGIN PRIVATE KEY-----\n").unwrap();

    // The mode a recreated file lands at is whatever this process's umask
    // says, so it is MEASURED here rather than assumed: on the agent's unit
    // (`UMask=0027`) it is 0640, on a developer's shell usually 0644. Either
    // way it is not 0600, which is the whole finding.
    let probe = root.path().join("probe");
    std::fs::write(&probe, "").unwrap();
    assert_eq!(mode_of(&key), mode_of(&probe));
    assert_ne!(mode_of(&key), 0o600);

    reassert_mode(&key, 0o600).unwrap();

    assert_eq!(mode_of(&key), 0o600);
}

#[test]
fn a_key_that_still_holds_its_mode_is_left_at_exactly_0600() {
    let root = tempfile::tempdir().unwrap();
    let key = root.path().join("privkey.pem");
    std::fs::write(&key, "-----BEGIN PRIVATE KEY-----\n").unwrap();
    std::fs::set_permissions(&key, std::fs::Permissions::from_mode(0o600)).unwrap();

    reassert_mode(&key, 0o600).unwrap();

    assert_eq!(mode_of(&key), 0o600);
}

#[test]
fn a_removal_that_committed_leaves_nothing_to_chmod_and_is_not_an_error() {
    let root = tempfile::tempdir().unwrap();
    let key = root.path().join("privkey.pem");

    reassert_mode(&key, 0o600).unwrap();

    // No file is invented in the process: the removal succeeded, and the state
    // to converge on is "not there".
    assert!(!key.exists());
}

/// A host whose web server refuses every configuration it is shown.
///
/// The one thing this test needs from a host: a validator that answers
/// non-zero, which is what makes `remove_config` roll its unlink back.
struct RefusingWebServer;

impl ConfigHost for RefusingWebServer {
    fn run(&self, _program: &str, _arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        Ok(CommandOutcome {
            status: 1,
            stdout: String::new(),
            stderr: "nginx: configuration file test failed".to_owned(),
        })
    }
}

#[test]
fn a_removal_the_web_server_refuses_puts_the_key_back_at_exactly_0600() {
    let root = tempfile::tempdir().unwrap();
    let key = root.path().join("privkey.pem");
    let material = "-----BEGIN PRIVATE KEY-----\n";
    std::fs::write(&key, material).unwrap();
    std::fs::set_permissions(&key, std::fs::Permissions::from_mode(0o600)).unwrap();

    let failure = remove_one(
        &RefusingWebServer,
        &key,
        0o600,
        &Validator {
            program: "/nonexistent",
            arguments: &[],
        },
        &Reload {
            program: "/nonexistent",
            arguments: &[],
        },
    )
    .unwrap_err();

    // The failure the caller asked about is the one it gets back, unchanged: a
    // mode that could not be restored must not replace the reason the removal
    // did not happen.
    assert!(matches!(failure, SslOpError::NginxValidation { .. }));
    // The rollback did happen — this is the state the mode question is about.
    assert_eq!(std::fs::read_to_string(&key).unwrap(), material);
    // And the key is at the mode it was written at, not at the umask's. The
    // rollback CREATED this file, so without the re-assertion it is
    // `0o666 & !umask` — 0640 under the agent unit's `UMask=0027`.
    assert_eq!(mode_of(&key), 0o600);
}
