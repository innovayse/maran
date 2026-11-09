//! Revalidating the inode quota a request carries.

use maran_agent_core::validation::system::inode_limit::InodeLimit;
use maran_ops::accounts::ResourceCeiling;

use crate::proto::{AgentError, ResourceCeiling as WireCeiling};
use crate::services::accounts::validated_ceiling::validated_ceiling;

/// Turns the request's wire inode ceiling into a validated one.
///
/// Shared by `CreateAccount` and `SetAccountQuota`, the two requests that put an
/// inode quota on an account: the agent's own check, and the one that decides,
/// because the figure ends up in a `setquota` that root runs
/// (rules/security.md item 1).
///
/// **An ABSENT ceiling is "unlimited", and that is the opposite of the slice
/// ceilings** (`validated_resource_limits`, where absent is refused). Both
/// requests existed before the field did, so an absent message is what every
/// caller written earlier sends, and for them "unlimited" is exactly what these
/// rpcs always did to the inode figure. A ceiling that IS present must still
/// state a case: one with neither set is refused, as is a figure of zero or one
/// the validated type refuses, so a caller that forgot a number is never turned
/// into an account with no inode ceiling.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming `inode_limit` when a present
/// ceiling sets neither case or carries a figure its validated type refuses.
pub fn validated_inode_limit(
    wire: Option<&WireCeiling>,
) -> Result<ResourceCeiling<InodeLimit>, AgentError> {
    match wire {
        None => Ok(ResourceCeiling::Unlimited),
        Some(_) => validated_ceiling("inode_limit", wire, InodeLimit::parse),
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/validated_inode_limit_tests.rs"]
mod tests;
