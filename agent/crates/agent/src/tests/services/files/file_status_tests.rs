//! Tests for the wire code every file failure travels as.
//!
//! One assertion per variant, because the codes are a CONTRACT — `files.proto`
//! names them to callers — and until review nothing in the suite asserted a
//! single one of them. The proto and the code had drifted apart in the same
//! change that wrote both, and flipping a code survived the whole workspace.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;
use maran_ops::files::FilesOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// The code `error` is reported as.
fn code_of(error: &FilesOpError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_file_that_is_not_there_is_not_found_so_a_retried_cleanup_reads_as_already_done() {
    assert_eq!(code_of(&FilesOpError::NotFound), ErrorCode::NotFound as i32);
}

#[test]
fn a_path_that_escapes_the_home_is_a_validation_failure_because_the_agent_had_to_look() {
    assert_eq!(
        code_of(&FilesOpError::EscapesHome),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_customers_tree_that_is_not_what_it_should_be_is_a_validation_failure_not_a_system_fault() {
    // The machine is fine; the account's own tree is not. Reporting these as a
    // system failure would send an operator looking at the host.
    for error in [
        FilesOpError::HomeUnusable,
        FilesOpError::DirectoryUnusable,
        FilesOpError::NotARegularFile,
    ] {
        assert_eq!(code_of(&error), ErrorCode::ValidationFailed as i32);
    }
}

#[test]
fn a_failure_of_the_machine_or_of_the_privileged_work_is_a_system_failure() {
    for error in [
        FilesOpError::WriteFailed,
        FilesOpError::RemoveFailed,
        FilesOpError::Privilege(PrivError::NoSuchAccount),
    ] {
        assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    }
}

#[test]
fn no_file_failure_ever_carries_tool_output_because_no_operation_here_spawns_a_tool() {
    assert!(
        to_agent_error(&FilesOpError::WriteFailed)
            .tool_output
            .is_empty()
    );
    assert!(
        to_agent_error(&FilesOpError::NotFound)
            .tool_output
            .is_empty()
    );
}
