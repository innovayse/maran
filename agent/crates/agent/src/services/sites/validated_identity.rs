//! Revalidating the account and the domain every site rpc carries.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::panel_host_names::PanelHostNames;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the account and the domain every site rpc carries.
///
/// The API validated them already. This is the agent's own check, and it
/// exists because the agent runs as root and the API does not
/// (rules/security.md item 1, which requires revalidation in the agent and
/// not only at the API boundary): the account becomes a uid to drop to and
/// the domain becomes a path segment, a `server_name` and a
/// certificate directory, so both are checked where they are used.
///
/// The reserved host-name set is enforced here, so this is the identity of a site
/// being created or re-served: the fixed set every host shares, and `panel`, the names this
/// host's own panel is served under (issue #468). The cleanup and read-only rpcs use
/// `validated_existing_identity`, which checks the shape alone.
///
/// # Errors
///
/// Returns the wire error for a name or a domain the agent will not accept.
pub fn validated_identity(
    account_username: &str,
    domain: &str,
    panel: &PanelHostNames,
) -> Result<(AccountName, Domain), AgentError> {
    let account =
        AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))?;
    let domain = Domain::parse(domain)
        .and_then(|domain| panel.check(domain))
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, domain))
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_identity_tests.rs"]
mod tests;
