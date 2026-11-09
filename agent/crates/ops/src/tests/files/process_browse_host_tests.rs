//! Tests for how a failed fork is read back as this area's vocabulary.
//!
//! The fork itself needs root and a real account (the polygon suite
//! `files_browse_on_a_real_host` drives it); what runs here is the mapping on both sides of the
//! exit status.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;

use super::ProcessBrowseHost;
use crate::files::FilesOpError;

#[test]
fn a_child_that_found_the_entry_absent_is_reported_as_not_found() {
    assert_eq!(
        ProcessBrowseHost::map_fork_error(PrivError::NoSuchEntry, FilesOpError::NotARegularFile),
        FilesOpError::NotFound
    );
}

/// The inverse control: an undifferentiated work failure is still the refusal the caller named.
#[test]
fn a_child_whose_work_failed_is_still_reported_as_the_refusal_the_caller_named() {
    assert_eq!(
        ProcessBrowseHost::map_fork_error(PrivError::WorkFailed, FilesOpError::NotARegularFile),
        FilesOpError::NotARegularFile
    );
    assert_eq!(
        ProcessBrowseHost::map_fork_error(PrivError::WorkFailed, FilesOpError::DirectoryUnusable),
        FilesOpError::DirectoryUnusable
    );
}

#[test]
fn only_an_absent_entry_crosses_the_drop_as_no_such_entry() {
    assert_eq!(
        ProcessBrowseHost::refusal_across_the_drop(FilesOpError::NotFound),
        PrivError::NoSuchEntry
    );

    for other in [
        FilesOpError::NotARegularFile,
        FilesOpError::DirectoryUnusable,
        FilesOpError::HomeUnusable,
        FilesOpError::TooLarge,
    ] {
        assert_eq!(
            ProcessBrowseHost::refusal_across_the_drop(other),
            PrivError::WorkFailed
        );
    }
}
