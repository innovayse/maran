//! Tests for the `directory` helpers.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;

use super::directory_size;

#[test]
fn a_path_that_does_not_exist_measures_zero() {
    let root = tempfile::tempdir().expect("a temporary directory can be made");

    assert_eq!(directory_size(&root.path().join("absent")), 0);
}

#[test]
fn an_empty_directory_measures_zero() {
    let root = tempfile::tempdir().expect("a temporary directory can be made");

    assert_eq!(directory_size(root.path()), 0);
}

#[test]
fn a_tree_measures_the_sum_of_its_files() {
    let root = tempfile::tempdir().expect("a temporary directory can be made");
    fs::write(root.path().join("a"), b"12345").expect("the file can be written");
    fs::create_dir(root.path().join("nested")).expect("the directory can be made");
    fs::write(root.path().join("nested/b"), b"123").expect("the file can be written");

    assert_eq!(directory_size(root.path()), 8);
}

#[test]
fn a_symlink_is_counted_as_nothing_and_never_followed() {
    // A link into / would make an account look enormous; a link to its own parent
    // would make the walk endless, which a customer can arrange with one command.
    let root = tempfile::tempdir().expect("a temporary directory can be made");
    fs::write(root.path().join("real"), b"12345").expect("the file can be written");
    std::os::unix::fs::symlink(root.path(), root.path().join("loop"))
        .expect("the link can be made");

    assert_eq!(directory_size(root.path()), 5);
}
