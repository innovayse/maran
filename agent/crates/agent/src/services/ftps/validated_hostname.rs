//! Rebuilding the optional hostname a status question carries.

use maran_agent_core::validation::web::domain::Domain;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the optional hostname a status question carries.
///
/// Empty means "report the daemon and range facts only", which is an answer and
/// not a refusal: a panel with no hostname persisted yet still gets one. A
/// non-empty value is revalidated for shape only (read-only status must keep
/// working for a legacy reserved name), because it selects a directory in
/// the agent's certificate store.
///
/// # Errors
///
/// Returns the wire error for a hostname the agent will not accept.
pub fn validated_hostname(hostname: &str) -> Result<Option<Domain>, AgentError> {
    match hostname {
        "" => Ok(None),
        candidate => Ok(Some(
            Domain::parse_existing(candidate).map_err(|error| invalid_input(error.to_string()))?,
        )),
    }
}

#[cfg(test)]
#[path = "../../tests/services/ftps/validated_hostname_tests.rs"]
mod tests;
