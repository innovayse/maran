//! Tests for `trashed_entry`: the id travels as its canonical spelling.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use super::trashed_entry;
use crate::proto::{AgentError, ErrorCode, trash_entry_response::Result as Outcome};

#[test]
fn a_success_carries_the_trash_id() {
    let response = trashed_entry(Ok(
        TrashEntryId::parse("1700000000-0123456789abcdef").unwrap()
    ));

    match response.result {
        Some(Outcome::Ok(ok)) => assert_eq!(ok.trash_id, "1700000000-0123456789abcdef"),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = trashed_entry(Err(AgentError {
        code: ErrorCode::NotFound as i32,
        message: "no such entry".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::NotFound as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
