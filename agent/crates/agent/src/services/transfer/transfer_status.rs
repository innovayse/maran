//! The one mapping from remote-source failures onto the wire error.

use maran_ops::transfer::TransferOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a failure of an operation against a registered source into the
/// `AgentError` the contract carries.
///
/// It lives beside the service so the match never grows into a handler, and so
/// one variant maps to one code in exactly one place (rules/rust.md "Service
/// anatomy"). The code says what the panel does next; WHICH connection failure it
/// was travels beside the error as a `SourceFailureReason`, chosen by
/// `source_failure_reason_of`, because the panel tells those four apart and the
/// shared error codes do not.
///
/// `tool_output` is EMPTY for every variant, and that is a property of
/// [`TransferOpError`] rather than an omission here: no variant carries a byte the
/// source printed, an argument vector or any part of the credential.
#[must_use]
pub fn to_agent_error(error: &TransferOpError) -> AgentError {
    let code = match error {
        // The administrator typed, or the name resolved to, an address the agent
        // never connects to: a fault of the request as sent.
        TransferOpError::AddressRefused => ErrorCode::InvalidInput,
        // The two refusals that leave both hosts exactly as they were, which is
        // what rules/proto.md defines VALIDATION_FAILED as. A changed host key is
        // never retried; a refused key is the operator's to fix on the source.
        TransferOpError::HostKeyChanged | TransferOpError::AuthRefused => {
            ErrorCode::ValidationFailed
        }
        // A deadline or an output cap ended a program and its process group was
        // killed: a fault of the exchange, never a verdict on the request. Named
        // on its own line so the census of bound-carrying errors can see the arm.
        TransferOpError::OutputBound(_) => ErrorCode::SystemFailure,
        // The source did not answer, or answered something that is not the
        // grammar of the command that asked, or this machine could not do its
        // part. None of these is the caller's fault.
        TransferOpError::Unreachable
        | TransferOpError::KeyGenerationFailed
        | TransferOpError::ScratchUnavailable
        | TransferOpError::HostAddressesUnavailable(_)
        | TransferOpError::Spawn(_)
        | TransferOpError::InvalidRemoteOutput => ErrorCode::SystemFailure,
        // This host has no room: the same refusal every writing area makes below
        // the free-space floor, and the panel stops the account instead of retrying.
        TransferOpError::DiskInsufficient => ErrorCode::BelowFreeSpaceFloor,
        // The dump outgrew the allowance the request stated: a size the request
        // did not allow, and not a fault of either host.
        TransferOpError::DumpTooLarge => ErrorCode::TooLarge,
        // The restricted loader refused a dump that leaves both hosts as they were.
        TransferOpError::LoadRefused => ErrorCode::ValidationFailed,
        // A failing copy, a failing dump, a server that refused the dump, a pull that
        // used up its allowance and a client that left are faults of the exchange.
        TransferOpError::PullFailed { .. }
        | TransferOpError::PullIncomplete
        | TransferOpError::DumpFailed { .. }
        | TransferOpError::LoadFailed
        | TransferOpError::Cancelled => ErrorCode::SystemFailure,
        // `TransferOpError` is #[non_exhaustive] (rules/rust.md), so a variant
        // added in the ops crate lands here rather than failing this build. A
        // system failure makes the panel report a fault instead of treating an
        // unclassified failure as a refusal and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/transfer_status_tests.rs"]
mod tests;
