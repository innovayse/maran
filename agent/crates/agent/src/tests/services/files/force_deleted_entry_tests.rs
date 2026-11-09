//! Tests for `force_deleted_entry`: a finished operation becomes the ok payload and a failure
//! travels as the typed error in the payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::force_deleted_entry;
use crate::proto::{AgentError, ErrorCode, force_delete_entry_response::Result as Outcome};

#[test]
fn a_success_carries_its_figures_in_the_ok_payload() {
    let response = force_deleted_entry(Ok(7));

    match response.result {
        Some(Outcome::Ok(ok)) => assert!(ok.entries_removed == 7),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = force_deleted_entry(Err(AgentError {
        code: ErrorCode::NotFound as i32,
        message: "no such entry".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::NotFound as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
