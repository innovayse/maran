//! Revalidating what a `PruneSiteLogs` request carries.

use maran_agent_core::validation::system::log_retention::LogRetention;
use maran_ops::sites::PruneLogsInput;

use crate::proto::{AgentError, PruneSiteLogsRequest};
use crate::services::sites::validated_existing_identity::validated_existing_identity;
use crate::services::wire::invalid_input::invalid_input;

/// Turns a request into the input of the log prune.
///
/// The retention is parsed into [`LogRetention`], which refuses `0` (what an
/// absent field decodes to, and would otherwise remove every archive) and
/// anything above the installer's shared policy. The account and the domain are
/// checked for shape only, so the logs of a site created under a name that is
/// reserved today can still be pruned.
///
/// # Errors
///
/// Returns the wire error for a name or a domain of the wrong shape and for a
/// retention outside `1..=14`.
pub fn validated_prune_request(
    request: &PruneSiteLogsRequest,
) -> Result<PruneLogsInput, AgentError> {
    let (account, domain) =
        validated_existing_identity(&request.account_username, &request.domain)?;
    let retain = LogRetention::parse(request.retain_rotations)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok(PruneLogsInput {
        account,
        domain,
        retain,
    })
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_prune_request_tests.rs"]
mod tests;
