//! Tests for `extracted_archive`: a finished extraction becomes the ok payload and a failure
//! travels as the typed error in the payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::archive_report::ArchiveReport;

use super::extracted_archive;
use crate::proto::{AgentError, ErrorCode, extract_archive_response::Result as Outcome};

#[test]
fn a_success_carries_the_member_count_in_the_ok_payload() {
    let response = extracted_archive(Ok(ArchiveReport {
        entries: 12,
        bytes: 900,
    }));

    match response.result {
        Some(Outcome::Ok(ok)) => assert_eq!(ok.entry_count, 12),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_count_too_big_for_the_wire_field_saturates_instead_of_reading_as_zero() {
    let response = extracted_archive(Ok(ArchiveReport {
        entries: u64::from(u32::MAX) + 5,
        bytes: 0,
    }));

    match response.result {
        Some(Outcome::Ok(ok)) => assert_eq!(ok.entry_count, u32::MAX),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = extracted_archive(Err(AgentError {
        code: ErrorCode::ArchiveUnsafe as i32,
        message: "the archive holds a member the agent will not write".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::ArchiveUnsafe as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
