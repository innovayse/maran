//! The closed list of refusals a child may report, and its round trip.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::FilesRefusal;
use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;

/// Every error a child can raise.
fn child_errors() -> Vec<FilesOpError> {
    vec![
        FilesOpError::HomeUnusable,
        FilesOpError::DirectoryUnusable,
        FilesOpError::NotFound,
        FilesOpError::AlreadyExists,
        FilesOpError::CrossesDevice,
        FilesOpError::TooDeep,
        FilesOpError::NotADirectory,
        FilesOpError::UnsupportedEntry,
        FilesOpError::NotOwned,
        FilesOpError::IntoItself,
        FilesOpError::LimitReached(LimitKind::Entries),
        FilesOpError::LimitReached(LimitKind::Bytes),
        FilesOpError::QuotaExceeded,
        FilesOpError::InvalidTrashEntry,
        FilesOpError::WriteFailed,
        FilesOpError::RemoveFailed,
        FilesOpError::MutationFailed,
        FilesOpError::NotARegularFile,
        FilesOpError::TooLarge,
    ]
}

#[test]
fn every_error_a_child_can_raise_comes_back_as_itself() {
    for error in child_errors() {
        let code = FilesRefusal::of(&error).code();

        let back = FilesRefusal::from_code(code).unwrap().into_error();

        assert_eq!(back, error);
    }
}

#[test]
fn the_codes_are_distinct_and_never_zero() {
    let mut codes: Vec<u8> = child_errors()
        .iter()
        .map(|error| FilesRefusal::of(error).code())
        .collect();
    assert!(!codes.contains(&0));

    codes.sort_unstable();
    codes.dedup();

    assert_eq!(codes.len(), child_errors().len());
}

#[test]
fn the_codes_stay_below_the_range_the_fork_keeps_for_its_own_use() {
    for error in child_errors() {
        assert!(FilesRefusal::of(&error).code() < 64);
    }
}

#[test]
fn a_byte_outside_the_list_is_not_trusted() {
    assert_eq!(FilesRefusal::from_code(0), None);
    assert_eq!(FilesRefusal::from_code(200), None);
}

#[test]
fn an_error_a_child_cannot_raise_reads_as_a_plain_failure() {
    assert_eq!(
        FilesRefusal::of(&FilesOpError::Saturated),
        FilesRefusal::MutationFailed
    );
}
