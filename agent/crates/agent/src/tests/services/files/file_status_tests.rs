//! Tests for the wire code every file failure travels as.
//!
//! One assertion per variant, because the codes are a CONTRACT — `files.proto`
//! names them to callers — and until review nothing in the suite asserted a
//! single one of them. The proto and the code had drifted apart in the same
//! change that wrote both, and flipping a code survived the whole workspace.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;
use maran_ops::files::FilesOpError;
use maran_ops::files::model::limit_kind::LimitKind;

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
        FilesOpError::ReadFailed,
        FilesOpError::MutationFailed,
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

/// `files` spawns no program, so it has no bound to map; a stray arm for one
/// would be noticed here.
#[test]
fn the_files_mapping_names_no_bound_variant() {
    const SOURCE: &str = include_str!("../../../services/files/file_status.rs");

    // Positive control: the scan reads the real file.
    assert!(SOURCE.contains("FilesOpError"));
    assert!(!SOURCE.contains("Bound("));
}

#[test]
fn an_upload_below_the_free_space_floor_travels_as_its_own_code() {
    let refusal = maran_agent_core::utils::floor_refusal::FloorRefusal::BelowFloor {
        available: 1,
        floor: 2,
    };

    assert_eq!(
        code_of(&FilesOpError::FreeSpace(refusal)),
        ErrorCode::BelowFreeSpaceFloor as i32
    );
}

#[test]
fn a_home_filesystem_that_cannot_be_measured_is_a_system_failure() {
    assert_eq!(
        code_of(&FilesOpError::FreeSpace(
            maran_agent_core::utils::floor_refusal::FloorRefusal::Unmeasurable
        )),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_directory_or_file_over_the_ceiling_travels_as_its_own_code() {
    assert_eq!(code_of(&FilesOpError::TooLarge), ErrorCode::TooLarge as i32);
}

#[test]
fn a_browse_refused_at_the_concurrency_bound_is_agent_saturated() {
    assert_eq!(
        code_of(&FilesOpError::Saturated),
        ErrorCode::AgentSaturated as i32
    );
}

#[test]
fn a_destination_that_is_taken_is_already_exists_so_the_panel_offers_another_name() {
    assert_eq!(
        code_of(&FilesOpError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_tree_the_operation_refuses_to_handle_is_a_validation_failure_not_a_system_fault() {
    for error in [
        FilesOpError::CrossesDevice,
        FilesOpError::TooDeep,
        FilesOpError::NotADirectory,
        FilesOpError::UnsupportedEntry,
        FilesOpError::InvalidTrashEntry,
        FilesOpError::NotOwned,
        FilesOpError::IntoItself,
    ] {
        assert_eq!(code_of(&error), ErrorCode::ValidationFailed as i32);
    }
}

#[test]
fn a_cap_the_request_stated_is_limit_reached_and_not_a_bound_of_the_agent() {
    assert_eq!(
        code_of(&FilesOpError::LimitReached(LimitKind::Entries)),
        ErrorCode::LimitReached as i32
    );
    assert_eq!(
        code_of(&FilesOpError::LimitReached(LimitKind::Bytes)),
        ErrorCode::LimitReached as i32
    );
}

#[test]
fn an_account_over_its_disk_quota_is_quota_exceeded_not_a_generic_failure() {
    assert_eq!(
        code_of(&FilesOpError::QuotaExceeded),
        ErrorCode::QuotaExceeded as i32
    );
}

#[test]
fn an_archive_the_agent_will_not_write_is_archive_unsafe_so_the_panel_can_say_so() {
    assert_eq!(
        code_of(&FilesOpError::ArchiveUnsafe),
        ErrorCode::ArchiveUnsafe as i32
    );
}

#[test]
fn a_file_that_is_not_the_archive_it_was_said_to_be_is_a_validation_failure() {
    assert_eq!(
        code_of(&FilesOpError::ArchiveUnreadable),
        ErrorCode::ValidationFailed as i32
    );
}
