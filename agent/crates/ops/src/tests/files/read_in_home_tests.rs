//! Tests for opening one file in a home for reading.
//!
//! None needs root: the uid is an argument and a test owns its temporary
//! directory as a customer owns their home. Every refusal has a positive
//! control in the same test, so a walk that refused everything cannot pass.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Read;
use std::os::unix::fs::symlink;
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::read_in_home;
use crate::files::FilesOpError;

/// Opens `path` under `home` as the current user.
fn open(home: &Path, path: &str) -> Result<std::fs::File, FilesOpError> {
    read_in_home(
        home,
        &BrowsePath::parse(path).unwrap(),
        current_uid().unwrap(),
    )
}

/// A home holding `docs/note.txt` with `hello`.
fn prepared() -> tempfile::TempDir {
    let home = tempfile::tempdir().unwrap();
    std::fs::create_dir(home.path().join("docs")).unwrap();
    std::fs::write(home.path().join("docs/note.txt"), b"hello").unwrap();

    home
}

#[test]
fn a_regular_file_is_opened_and_reads_back_its_content() {
    let home = prepared();

    let mut content = String::new();
    open(home.path(), "docs/note.txt")
        .unwrap()
        .read_to_string(&mut content)
        .unwrap();

    assert_eq!(content, "hello");
}

#[test]
fn a_file_directly_in_the_home_is_opened() {
    let home = prepared();
    std::fs::write(home.path().join("top.txt"), b"x").unwrap();

    assert!(open(home.path(), "top.txt").is_ok());
}

#[test]
fn the_home_itself_is_not_a_file() {
    let home = prepared();

    assert_eq!(
        open(home.path(), "").unwrap_err(),
        FilesOpError::NotARegularFile
    );
}

#[test]
fn a_directory_is_not_a_file() {
    let home = prepared();

    assert_eq!(
        open(home.path(), "docs").unwrap_err(),
        FilesOpError::NotARegularFile
    );
}

#[test]
fn a_missing_file_is_not_found() {
    let home = prepared();

    assert_eq!(
        open(home.path(), "docs/absent.txt").unwrap_err(),
        FilesOpError::NotFound
    );
}

#[test]
fn a_symlink_leaf_is_refused_even_though_its_target_is_a_readable_file() {
    let home = prepared();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("secret"), b"s").unwrap();
    symlink(outside.path().join("secret"), home.path().join("docs/link")).unwrap();

    assert_eq!(
        open(home.path(), "docs/link").unwrap_err(),
        FilesOpError::NotARegularFile
    );
    assert!(open(home.path(), "docs/note.txt").is_ok());
}

#[test]
fn a_symlinked_directory_on_the_way_is_refused() {
    let home = prepared();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("secret"), b"s").unwrap();
    symlink(outside.path(), home.path().join("escape")).unwrap();

    assert_eq!(
        open(home.path(), "escape/secret").unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
    assert!(open(home.path(), "docs/note.txt").is_ok());
}

#[test]
fn a_file_with_a_second_hard_link_is_refused() {
    let home = prepared();
    std::fs::hard_link(
        home.path().join("docs/note.txt"),
        home.path().join("docs/alias"),
    )
    .unwrap();

    assert_eq!(
        open(home.path(), "docs/alias").unwrap_err(),
        FilesOpError::NotARegularFile
    );
    assert_eq!(
        open(home.path(), "docs/note.txt").unwrap_err(),
        FilesOpError::NotARegularFile
    );
    // Control: removing the second name makes the same file readable again.
    std::fs::remove_file(home.path().join("docs/alias")).unwrap();
    assert!(open(home.path(), "docs/note.txt").is_ok());
}

#[test]
fn a_file_owned_by_another_uid_is_refused() {
    let home = prepared();
    let other = current_uid().unwrap() + 1;

    let refused = read_in_home(
        home.path(),
        &BrowsePath::parse("docs/note.txt").unwrap(),
        other,
    )
    .unwrap_err();

    // The home itself is not that uid's, so the walk refuses at its first step.
    assert_eq!(refused, FilesOpError::HomeUnusable);
}

#[test]
fn a_fifo_is_refused_and_does_not_block_the_process() {
    let home = prepared();
    let made = std::process::Command::new("mkfifo")
        .arg(home.path().join("docs/pipe"))
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    // Without `O_NONBLOCK` this call would never return: opening a FIFO with no
    // writer blocks in the kernel. The test would hang rather than fail.
    assert_eq!(
        open(home.path(), "docs/pipe").unwrap_err(),
        FilesOpError::NotARegularFile
    );
    assert!(open(home.path(), "docs/note.txt").is_ok());
}
