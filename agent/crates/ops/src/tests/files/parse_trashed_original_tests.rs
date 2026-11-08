//! A recorded original path is validated like any path from outside.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_trashed_original;
use crate::files::FilesOpError;

#[test]
fn an_ordinary_recorded_path_is_accepted() {
    assert!(parse_trashed_original(b"sites/example.com/index.html").is_ok());
}

#[test]
fn a_forged_record_with_traversal_is_refused() {
    assert_eq!(
        parse_trashed_original(b"../../etc/passwd").unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_forged_record_naming_the_trash_itself_is_refused() {
    assert_eq!(
        parse_trashed_original(b".maran-trash/entries").unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn an_empty_record_is_refused_because_it_would_name_the_home() {
    assert_eq!(
        parse_trashed_original(b"").unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_record_that_is_not_utf8_is_refused() {
    assert_eq!(
        parse_trashed_original(&[0xff, 0xfe]).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}
