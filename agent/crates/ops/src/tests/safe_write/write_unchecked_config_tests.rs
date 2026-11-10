//! Tests for [`write_unchecked_config`]: the protocol with no validator and no
//! reload, for a file no tool can check.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;

use crate::safe_write::SafeWriteError;
use crate::safe_write::model::ConfigFile;
use crate::safe_write::write_unchecked_config;

#[test]
fn the_file_lands_at_its_mode_with_its_content() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("Kexample.com.+013+12345.private");

    write_unchecked_config(&ConfigFile {
        target: &target,
        contents: "Private-key-format: v1.3\n",
        mode: 0o640,
    })
    .unwrap();

    assert_eq!(
        std::fs::read_to_string(&target).unwrap(),
        "Private-key-format: v1.3\n"
    );
    assert_eq!(
        std::fs::metadata(&target).unwrap().permissions().mode() & 0o777,
        0o640
    );
}

#[test]
fn writing_over_an_existing_file_replaces_it_whole() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("key");
    std::fs::write(&target, "old\n").unwrap();

    write_unchecked_config(&ConfigFile {
        target: &target,
        contents: "new\n",
        mode: 0o640,
    })
    .unwrap();

    assert_eq!(std::fs::read_to_string(&target).unwrap(), "new\n");
}

#[test]
fn no_temporary_file_is_left_beside_the_target() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("key");

    write_unchecked_config(&ConfigFile {
        target: &target,
        contents: "content\n",
        mode: 0o640,
    })
    .unwrap();

    let names: Vec<_> = std::fs::read_dir(directory.path())
        .unwrap()
        .map(|entry| entry.unwrap().file_name())
        .collect();
    assert_eq!(names, vec![std::ffi::OsString::from("key")]);
}

#[test]
fn a_target_in_a_directory_that_is_not_there_is_refused_and_creates_nothing() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("absent").join("key");

    let refused = write_unchecked_config(&ConfigFile {
        target: &target,
        contents: "content\n",
        mode: 0o640,
    });

    assert!(matches!(refused, Err(SafeWriteError::TemporaryWrite)));
    assert!(!directory.path().join("absent").exists());
}
