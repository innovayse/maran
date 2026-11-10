//! Tests for settling the audit slot from a remote-source answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;

use super::settle_source_call;
use crate::audit::audit_outcome::AuditOutcome;
use crate::audit::audit_slot::AuditSlot;
use crate::proto::ErrorCode;
use crate::services::transfer::source_failure::SourceFailure;

#[test]
fn a_success_settles_the_slot_as_ok() {
    let slot = AuditSlot::of(&http::Extensions::new());

    settle_source_call::<()>(&slot, &Ok(()));

    assert_eq!(slot.settled_outcome(), Some(AuditOutcome::Ok));
}

#[test]
fn a_failure_settles_the_slot_with_the_code_of_its_error() {
    for (failure, code) in [
        (TransferOpError::AddressRefused, ErrorCode::InvalidInput),
        (TransferOpError::HostKeyChanged, ErrorCode::ValidationFailed),
        (TransferOpError::Unreachable, ErrorCode::SystemFailure),
    ] {
        let slot = AuditSlot::of(&http::Extensions::new());

        settle_source_call::<()>(&slot, &Err(SourceFailure::of(&failure)));

        assert_eq!(slot.settled_outcome(), Some(AuditOutcome::Refused(code)));
    }
}

#[test]
fn a_code_the_contract_does_not_know_is_recorded_as_unspecified() {
    let slot = AuditSlot::of(&http::Extensions::new());
    let mut failure = SourceFailure::of(&TransferOpError::Unreachable);
    failure.error.code = 9_999;

    settle_source_call::<()>(&slot, &Err(failure));

    assert_eq!(
        slot.settled_outcome(),
        Some(AuditOutcome::Refused(ErrorCode::Unspecified))
    );
}
