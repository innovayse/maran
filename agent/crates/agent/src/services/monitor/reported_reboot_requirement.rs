//! What a reboot-requirement reading looks like on the wire.

use maran_distro::RebootRequirement;

use crate::proto::{
    AgentError, GetRebootRequirementOk, RebootRequirement as WireRequirement,
    get_reboot_requirement_response,
};

/// Maps one `GetRebootRequirement` outcome onto the response's `oneof`.
///
/// A probe the agent could not run travels as the error arm, never as
/// `NOT_REQUIRED`: "could not find out" and "no reboot needed" are different
/// answers, and the panel tells the operator a host is safe to leave on the
/// second only (issue #163). `UNSPECIFIED` is never sent for a reading that was
/// taken.
#[must_use]
pub fn reported_reboot_requirement(
    outcome: Result<RebootRequirement, AgentError>,
) -> get_reboot_requirement_response::Result {
    let requirement = match outcome {
        Ok(RebootRequirement::Required) => WireRequirement::Required,
        Ok(RebootRequirement::NotRequired) => WireRequirement::NotRequired,
        Err(error) => return get_reboot_requirement_response::Result::Error(error),
    };

    get_reboot_requirement_response::Result::Ok(GetRebootRequirementOk {
        requirement: requirement as i32,
    })
}

#[cfg(test)]
#[path = "../../tests/services/monitor/reported_reboot_requirement_tests.rs"]
mod tests;
