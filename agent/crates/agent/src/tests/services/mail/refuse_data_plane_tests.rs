//! The refusal a data-plane rpc answers with while its operation is not carried.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::audit::audit_outcome::AuditOutcome;
use crate::audit::audit_slot::AuditSlot;
use crate::proto::ErrorCode;
use crate::services::mail::refuse_data_plane::refuse_data_plane;

#[test]
fn the_refusal_is_a_typed_not_implemented_error_without_tool_output() {
    let error = refuse_data_plane(&http::Extensions::new());

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
    assert!(!error.message.is_empty());
    assert!(error.tool_output.is_empty());
}

#[test]
fn the_audit_slot_of_the_request_is_settled_with_the_same_code() {
    let mut extensions = http::Extensions::new();
    let slot = AuditSlot::install(&mut extensions);

    let error = refuse_data_plane(&extensions);

    assert_eq!(error.code, ErrorCode::NotImplemented as i32);
    assert_eq!(
        slot.settled_outcome(),
        Some(AuditOutcome::Refused(ErrorCode::NotImplemented))
    );
}
