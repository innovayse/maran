//! Rebuilding the description of a site that may already exist.

use maran_agent_core::validation::web::domain::Domain;
use maran_ops::sites::CreateSiteInput;

use crate::proto::{AgentError, SiteSpec};
use crate::services::sites::validated_existing_identity::validated_existing_identity;
use crate::services::sites::validated_site::site_from_spec;

/// Rebuilds the site description for a cleanup rpc (disable, remove
/// certificate) without the reserved-name refusal.
///
/// Identical to [`super::validated_site::validated_site`] except that the
/// domain and each alias are checked for shape only, so a site created under
/// a name that is reserved today can still be wound down after an upgrade.
///
/// # Errors
///
/// Returns the wire error when the spec is absent or any part of it is
/// refused.
pub fn validated_existing_site(
    account_username: &str,
    domain: &str,
    spec: Option<&SiteSpec>,
) -> Result<CreateSiteInput, AgentError> {
    let identity = validated_existing_identity(account_username, domain)?;

    site_from_spec(identity, spec, Domain::parse_existing)
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_existing_site_tests.rs"]
mod tests;
