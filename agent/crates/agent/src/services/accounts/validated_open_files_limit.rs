//! Revalidating the open-files limit a `SetAccountOpenFilesLimit` request
//! carries.

use maran_agent_core::validation::system::open_files_limit::OpenFilesLimit;
use maran_ops::accounts::ResourceCeiling;

use crate::proto::{AgentError, SetAccountOpenFilesLimitRequest};
use crate::services::accounts::validated_ceiling::validated_ceiling;

/// Turns the request's wire ceiling into a validated one.
///
/// The panel validated it already; this is the agent's own check, and the one
/// that decides (rules/security.md item 1), because the figure ends up in a file
/// root writes under the host's PAM limits directory.
///
/// **An absent ceiling is refused, never read as "unlimited"**, the same law as
/// `SetAccountResourceLimits`: a caller that forgot the field must not remove an
/// account's ceiling by omission. "No ceiling" has its own case on the wire and
/// only that case removes the drop-in.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming `open_files` when the ceiling is
/// absent, sets neither case, or carries a figure its validated type refuses
/// (zero, or above the maximum).
pub fn validated_open_files_limit(
    request: &SetAccountOpenFilesLimitRequest,
) -> Result<ResourceCeiling<OpenFilesLimit>, AgentError> {
    validated_ceiling(
        "open_files",
        request.open_files.as_ref(),
        OpenFilesLimit::parse,
    )
}

#[cfg(test)]
#[path = "../../tests/services/accounts/validated_open_files_limit_tests.rs"]
mod tests;
