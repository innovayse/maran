//! A failed remote-source call: the wire error and the reason the panel acts on.

use maran_ops::transfer::TransferOpError;

use crate::proto::{AgentError, SourceFailureReason, TransferFailureReason};
use crate::services::transfer::source_failure_reason_of::source_failure_reason_of;
use crate::services::transfer::transfer_failure_reason_of::transfer_failure_reason_of;
use crate::services::transfer::transfer_status::to_agent_error;

/// Both halves of a failed remote-source call's answer.
#[derive(Debug)]
pub struct SourceFailure {
    /// The typed error, as every rpc answers.
    pub error: AgentError,
    /// Which connection failure it was; unspecified for a failure of this host.
    pub reason: SourceFailureReason,
    /// Which failure of a transfer it was, for the rpcs that move data;
    /// unspecified for every other failure.
    pub transfer_reason: TransferFailureReason,
}

impl SourceFailure {
    /// The failure an operation's error is.
    #[must_use]
    pub fn of(error: &TransferOpError) -> Self {
        Self {
            error: to_agent_error(error),
            reason: source_failure_reason_of(error),
            transfer_reason: transfer_failure_reason_of(error),
        }
    }

    /// A failure with no connection reason: a malformed request or a host fault
    /// the service layer itself detected.
    #[must_use]
    pub fn plain(error: AgentError) -> Self {
        Self {
            error,
            reason: SourceFailureReason::Unspecified,
            transfer_reason: TransferFailureReason::Unspecified,
        }
    }

    /// A request refused for naming an address the agent never connects to,
    /// decided from the text alone before anything was resolved.
    #[must_use]
    pub fn address_refused(error: AgentError) -> Self {
        Self {
            error,
            reason: SourceFailureReason::AddressRefused,
            transfer_reason: TransferFailureReason::Unspecified,
        }
    }
}
