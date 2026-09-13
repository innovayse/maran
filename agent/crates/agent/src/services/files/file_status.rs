//! The one mapping from customer-file operation failures onto the wire error.

use maran_ops::files::FilesOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a file operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **Nothing here can carry a path.** That is a property of [`FilesOpError`],
/// which has no variant that holds one — every path this area touches is inside
/// a hosting customer's home — and the mapping stays inside that guarantee by
/// copying only what the enum defines and inventing no message of its own.
/// `tool_output` is empty for every variant, because no operation in this area
/// spawns a tool.
#[must_use]
pub fn to_agent_error(error: &FilesOpError) -> AgentError {
    let code = match error {
        // `files.proto`: removing a path that is not there returns NotFound,
        // which the panel reads as "already done" on a retried cleanup.
        FilesOpError::NotFound => ErrorCode::NotFound,
        // The containment check refused the path. VALIDATION_FAILED and not
        // INVALID_INPUT: the request was well formed and the AGENT's own check
        // is what refused it, after looking at the filesystem — which is the
        // distinction rules/proto.md draws between the two codes.
        FilesOpError::EscapesHome => ErrorCode::ValidationFailed,
        // Something is in the customer's home that should not be there, or
        // something that should be is not: a symlink where a directory belongs,
        // a FIFO where the challenge belongs, a home that is not the account's.
        // Reported as a validation failure rather than a system fault, because
        // the machine is fine and the account's tree is not.
        FilesOpError::HomeUnusable
        | FilesOpError::DirectoryUnusable
        | FilesOpError::NotARegularFile => ErrorCode::ValidationFailed,
        // The machine, or the privileged work on it, is what failed.
        FilesOpError::Privilege(_) | FilesOpError::WriteFailed | FilesOpError::RemoveFailed => {
            ErrorCode::SystemFailure
        }
        // FilesOpError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps
        // to a system failure: the panel then reports a fault instead of
        // silently treating an unclassified failure as "not found" and carrying
        // on with an issuance whose challenge was never written.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/file_status_tests.rs"]
mod tests;
