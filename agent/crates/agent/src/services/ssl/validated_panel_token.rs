//! The agent's own re-check of a panel challenge token.

use maran_agent_core::validation::web::acme_token::AcmeToken;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Validates the token a panel challenge rpc carries.
///
/// The panel already refused anything but base64url; this is the agent's own
/// check (rules/architecture.md "Every input is re-validated inside the
/// agent"), and the one that decides, because the token becomes a file name
/// written as root.
///
/// # Errors
///
/// Returns `ERROR_CODE_INVALID_INPUT` naming the refusal.
pub fn validated_panel_token(token: &str) -> Result<AcmeToken, AgentError> {
    AcmeToken::parse(token).map_err(|error| invalid_input(error.to_string()))
}
