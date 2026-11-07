//! The agent's own re-check of the panel's host name.

use maran_agent_core::validation::web::domain::Domain;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Validates the host name a restore of the panel's self-signed certificate
/// carries.
///
/// The panel sends its own configured name; this is the agent's check
/// (rules/architecture.md "Every input is re-validated inside the agent"),
/// because the name becomes the subject and the alternative name openssl parses.
/// `parse_existing`, not `parse`: the panel's host name is a name the installer
/// already serves, and the reserved set `parse` adds is for names about to be
/// created. Both refuse every character openssl's `-subj` and `-addext` treat
/// as syntax.
///
/// # Errors
///
/// Returns `ERROR_CODE_INVALID_INPUT` naming the refusal.
pub fn validated_panel_host_name(host_name: &str) -> Result<Domain, AgentError> {
    Domain::parse_existing(host_name).map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/ssl/validated_panel_host_name_tests.rs"]
mod tests;
