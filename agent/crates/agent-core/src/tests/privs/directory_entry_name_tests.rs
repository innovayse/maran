//! Tests for the last check before a name reaches an `*at` syscall.
//!
//! This function is the shared guard of five wrappers, so a hole in it is a
//! hole in all of them at once — including the ones that CREATE and REMOVE
//! things inside a customer's home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::{OsStr, OsString};
use std::io;
use std::os::unix::ffi::OsStringExt;

use super::directory_entry_name;

#[test]
fn an_ordinary_entry_name_is_accepted_and_becomes_a_c_string() {
    let name = directory_entry_name(OsStr::new("token123")).unwrap();

    assert_eq!(name.to_bytes(), b"token123");
}

#[test]
fn an_empty_name_is_refused() {
    let refused = directory_entry_name(OsStr::new("")).unwrap_err();

    assert_eq!(refused.kind(), io::ErrorKind::InvalidInput);
}

#[test]
fn a_name_with_a_separator_is_refused_because_it_is_a_path_and_not_a_name() {
    assert_eq!(
        directory_entry_name(OsStr::new("../etc/shadow"))
            .unwrap_err()
            .kind(),
        io::ErrorKind::InvalidInput
    );
    assert_eq!(
        directory_entry_name(OsStr::new("a/b")).unwrap_err().kind(),
        io::ErrorKind::InvalidInput
    );
    assert_eq!(
        directory_entry_name(OsStr::new("/absolute"))
            .unwrap_err()
            .kind(),
        io::ErrorKind::InvalidInput
    );
}

#[test]
fn the_two_traversal_names_are_refused_on_their_own() {
    assert_eq!(
        directory_entry_name(OsStr::new(".")).unwrap_err().kind(),
        io::ErrorKind::InvalidInput
    );
    assert_eq!(
        directory_entry_name(OsStr::new("..")).unwrap_err().kind(),
        io::ErrorKind::InvalidInput
    );
}

#[test]
fn an_interior_nul_is_refused_rather_than_silently_truncating_the_name() {
    let name = OsString::from_vec(b"token\0.txt".to_vec());

    assert_eq!(
        directory_entry_name(&name).unwrap_err().kind(),
        io::ErrorKind::InvalidInput
    );
}

#[test]
fn a_dotfile_is_not_a_traversal_and_is_accepted() {
    let name = directory_entry_name(OsStr::new(".well-known")).unwrap();

    assert_eq!(name.to_bytes(), b".well-known");
}
