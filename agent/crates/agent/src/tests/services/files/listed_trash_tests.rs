//! Tests for `listed_trash`: items keep their order and a failure travels in
//! the payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::trashed_entry::TrashedEntry as TrashedItem;

use super::listed_trash;
use crate::proto::{AgentError, ErrorCode, list_trash_response::Result as Outcome};

/// An item whose id and path both carry `n`.
fn item(n: u64) -> TrashedItem {
    TrashedItem {
        trash_id: format!("{}-0123456789abcdef", 1_700_000_000 + n),
        original_path: format!("sites/{n}"),
        trashed_at_unix: 1_700_000_000 + n as i64,
        is_directory: false,
        size_bytes: n,
    }
}

#[test]
fn items_reach_the_wire_in_the_order_the_operation_returned_them() {
    let response = listed_trash(Ok(vec![item(2), item(1)]));

    match response.result {
        Some(Outcome::Ok(ok)) => {
            let paths: Vec<_> = ok
                .entries
                .iter()
                .map(|e| e.original_path.as_str())
                .collect();
            assert_eq!(paths, ["sites/2", "sites/1"]);
        }
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn an_empty_trash_is_an_ok_payload_with_no_entries() {
    match listed_trash(Ok(Vec::new())).result {
        Some(Outcome::Ok(ok)) => assert!(ok.entries.is_empty()),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = listed_trash(Err(AgentError {
        code: ErrorCode::SystemFailure as i32,
        message: "failed".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::SystemFailure as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
