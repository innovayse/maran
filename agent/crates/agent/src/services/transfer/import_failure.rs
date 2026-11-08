//! A failed import: the wire error and the reason the panel acts on.

use maran_ops::backup::BackupError;

use crate::proto::{AgentError, ImportRefusalReason};
use crate::services::backup::backup_status::to_agent_error;
use crate::services::transfer::refusal_reason_of::refusal_reason_of;

/// Both halves of a failed import's answer.
pub struct ImportFailure {
    /// The typed error, as every rpc answers.
    pub error: AgentError,
    /// Which refusal it was; unspecified for a failure of the host.
    pub reason: ImportRefusalReason,
}

impl ImportFailure {
    /// The failure an operation's error is.
    #[must_use]
    pub fn of(error: &BackupError) -> Self {
        Self {
            error: to_agent_error(error),
            reason: refusal_reason_of(error),
        }
    }

    /// A failure with no refusal reason: a malformed stream or a host fault the
    /// service layer itself detected.
    #[must_use]
    pub fn plain(error: AgentError) -> Self {
        Self {
            error,
            reason: ImportRefusalReason::Unspecified,
        }
    }
}
