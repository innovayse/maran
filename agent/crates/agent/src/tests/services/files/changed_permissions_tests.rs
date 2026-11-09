//! Tests for `changed_permissions`: a finished operation becomes the ok payload and a failure
//! travels as the typed error in the payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::changed_permissions;
use crate::proto::{AgentError, ErrorCode, change_permissions_response::Result as Outcome};
use maran_ops::files::model::permission_report::PermissionReport;

#[test]
fn a_success_carries_its_figures_in_the_ok_payload() {
    let response = changed_permissions(Ok(PermissionReport {
        changed: 4,
        skipped: 2,
    }));

    match response.result {
        Some(Outcome::Ok(ok)) => assert!(ok.changed == 4 && ok.skipped == 2),
        other => panic!("expected the ok payload, got {other:?}"),
    }
}

#[test]
fn a_failure_carries_the_typed_error_in_the_payload() {
    let response = changed_permissions(Err(AgentError {
        code: ErrorCode::NotFound as i32,
        message: "no such entry".to_owned(),
        tool_output: String::new(),
    }));

    match response.result {
        Some(Outcome::Error(error)) => assert_eq!(error.code, ErrorCode::NotFound as i32),
        other => panic!("expected the error payload, got {other:?}"),
    }
}
