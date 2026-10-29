//! Revalidating the account and the domain of a site that may already exist.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the account and the domain of a site that may already exist,
/// without the reserved-name refusal.
///
/// Used by the cleanup and read-only rpcs (delete, log tail, disable, remove
/// certificate), so a site created under a name that is reserved today can
/// still be removed. The shape checks that protect the path and the config
/// file still apply in full.
///
/// # Errors
///
/// Returns the wire error for a name or a domain of the wrong shape.
pub fn validated_existing_identity(
    account_username: &str,
    domain: &str,
) -> Result<(AccountName, Domain), AgentError> {
    let account =
        AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))?;
    let domain =
        Domain::parse_existing(domain).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, domain))
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_existing_identity_tests.rs"]
mod tests;
