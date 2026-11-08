//! Tests for `created_archive`: a finished creation becomes the ok payload and a failure
//! travels as the typed error in the payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::archive_report::ArchiveReport;

use super::created_archive;
use crate::proto::{AgentError, ErrorCode, create_archive_response::Result as Outcome};

#[test]
fn a_success_carries_the_size_of_the_archive_in_the_ok_payload() {
    let response = created_archive(Ok(ArchiveReport {
        entries: 9,
        bytes: 4096,
    }));

    match response.result {
        Some(Outcome::Ok(ok)) => assert_eq!(ok.size_bytes, 4096),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = created_archive(Err(AgentError {
        code: ErrorCode::AlreadyExists as i32,
        message: "the destination already exists".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::AlreadyExists as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
