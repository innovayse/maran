//! The audit slot settled from the answer a remote-source rpc is about to send.

use crate::audit::audit_slot::AuditSlot;
use crate::proto::ErrorCode;
use crate::services::transfer::source_failure::SourceFailure;

/// Settles `audit` as a success or as the refusal whose code `result` carries.
///
/// A named step and not a call to `AuditSlot::settle`, because that takes a result
/// whose error is an `AgentError` and the source rpcs carry a [`SourceFailure`]
/// (the error plus the connection reason). Only the code reaches the slot: the
/// reason is the panel's to act on and the audit line has no place for it. A code
/// the contract does not know is recorded as unspecified, never printed.
pub fn settle_source_call<T>(audit: &AuditSlot, result: &Result<T, SourceFailure>) {
    match result {
        Ok(_) => audit.ok(),
        Err(failure) => {
            audit.refused(ErrorCode::try_from(failure.error.code).unwrap_or(ErrorCode::Unspecified))
        }
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/settle_source_call_tests.rs"]
mod tests;
