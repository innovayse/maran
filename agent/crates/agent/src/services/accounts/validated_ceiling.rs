//! Reading one wire ceiling through a validated type.

use std::fmt::Display;

use maran_ops::accounts::ResourceCeiling;

use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{AgentError, ResourceCeiling as WireCeiling};
use crate::services::wire::invalid_input::invalid_input;

/// Reads one wire ceiling through `parse`, naming `field` in any refusal.
///
/// The one place a [`WireCeiling`] becomes a [`ResourceCeiling`], shared by
/// every request that carries one (the slice ceilings, the open-files limit and
/// the inode quota), so the rule below is written once: a number that was never
/// set decodes to zero and a message that was never set is absent, and neither
/// is "no ceiling" - which has its own case on the wire and is the only thing
/// that means it. Whether an ABSENT ceiling is itself an answer is the
/// caller's decision: the slice and the open-files rpc refuse it, the inode
/// quota of a creation reads it as "unlimited" for a caller that predates the
/// field.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming `field` when the ceiling is
/// absent, sets neither case, or `parse` refuses its figure.
pub fn validated_ceiling<T, E: Display>(
    field: &str,
    wire: Option<&WireCeiling>,
    parse: impl Fn(u32) -> Result<T, E>,
) -> Result<ResourceCeiling<T>, AgentError> {
    match wire.and_then(|ceiling| ceiling.ceiling.as_ref()) {
        Some(Ceiling::Unlimited(_)) => Ok(ResourceCeiling::Unlimited),
        Some(Ceiling::Maximum(figure)) => parse(*figure)
            .map(ResourceCeiling::Limited)
            .map_err(|error| invalid_input(format!("{field}: {error}"))),
        None => Err(invalid_input(format!(
            "{field}: a ceiling is required; send `unlimited` for none"
        ))),
    }
}
