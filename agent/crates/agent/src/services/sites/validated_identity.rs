//! Revalidating the account and the domain every site rpc carries.

use maran_agent_core::validation::domain::Domain;
use maran_agent_core::validation::name::AccountName;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// Revalidates the account and the domain every site rpc carries.
///
/// The API validated them already. This is the agent's own check, and it
/// exists because the agent runs as root and the API does not
/// (rules/security.md "Undistrust of the caller"): the account becomes a uid
/// to drop to and the domain becomes a path segment, a `server_name` and a
/// certificate directory, so both are checked where they are used.
///
/// # Errors
///
/// Returns the wire error for a name or a domain the agent will not accept.
pub fn validated_identity(
    account_username: &str,
    domain: &str,
) -> Result<(AccountName, Domain), AgentError> {
    let account =
        AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))?;
    let domain = Domain::parse(domain).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, domain))
}
