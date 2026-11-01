//! Revalidating the three slice ceilings a `SetAccountResourceLimits` request
//! carries.

use std::fmt::Display;

use maran_agent_core::validation::system::cpu_quota_percent::CpuQuotaPercent;
use maran_agent_core::validation::system::memory_max_mib::MemoryMaxMib;
use maran_agent_core::validation::system::tasks_max::TasksMax;
use maran_ops::accounts::{AccountResourceLimits, ResourceCeiling};

use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{AgentError, ResourceCeiling as WireCeiling, SetAccountResourceLimitsRequest};
use crate::services::wire::invalid_input::invalid_input;

/// Turns the request's three wire ceilings into validated ones.
///
/// The panel validated them already; this is the agent's own check, and the one
/// that decides (rules/security.md item 1), because every figure here ends up
/// in a `systemctl set-property` that root runs.
///
/// **An absent ceiling is refused, never read as "unlimited".** Protobuf reads a
/// message the sender never set as absent and a number it never set as zero, so
/// both are what a caller that forgot a field sends; "no ceiling" has its own
/// case on the wire, and only that case means it.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming the field when a ceiling is
/// absent, sets neither case, or carries a figure its validated type refuses
/// (zero, or above that type's maximum).
pub fn validated_resource_limits(
    request: &SetAccountResourceLimitsRequest,
) -> Result<AccountResourceLimits, AgentError> {
    Ok(AccountResourceLimits {
        cpu: ceiling(
            "cpu_quota_percent",
            request.cpu_quota_percent.as_ref(),
            CpuQuotaPercent::parse,
        )?,
        memory: ceiling(
            "memory_max_mib",
            request.memory_max_mib.as_ref(),
            MemoryMaxMib::parse,
        )?,
        tasks: ceiling("tasks_max", request.tasks_max.as_ref(), TasksMax::parse)?,
    })
}

/// Reads one wire ceiling through `parse`, naming `field` in any refusal.
///
/// # Errors
///
/// Returns the wire's invalid-input error when the ceiling is absent, sets
/// neither case, or `parse` refuses its figure.
fn ceiling<T, E: Display>(
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

#[cfg(test)]
#[path = "../../tests/services/accounts/validated_resource_limits_tests.rs"]
mod tests;
