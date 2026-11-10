//! Revalidating the app id of an app rpc.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use crate::proto::{AgentError, ErrorCode};

/// Parses the app id of a request against the closed catalogue.
///
/// The API validated it already. This is the agent's own check, and it exists because the agent
/// runs as root and the API does not (rules/security.md item 1): the id decides which package a
/// root unit installs or removes.
///
/// # Errors
///
/// Returns the wire error for an id outside the catalogue.
pub fn validated_app_id(candidate: &str) -> Result<ValidatedAppId, AgentError> {
    ValidatedAppId::parse(candidate).map_err(|error| AgentError {
        code: ErrorCode::InvalidInput as i32,
        message: error.to_string(),
        tool_output: String::new(),
    })
}

#[cfg(test)]
#[path = "../../tests/services/apps/validated_app_id_tests.rs"]
mod tests;
