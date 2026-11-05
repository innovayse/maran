//! Tests for how a reboot-requirement reading reaches the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::RebootRequirement;

use super::reported_reboot_requirement;
use crate::proto::{
    AgentError, ErrorCode, GetRebootRequirementOk, RebootRequirement as WireRequirement,
    get_reboot_requirement_response,
};

/// The requirement a successful reading carries on the wire, or a panic naming
/// the error.
fn wire_requirement(outcome: Result<RebootRequirement, AgentError>) -> i32 {
    match reported_reboot_requirement(outcome) {
        get_reboot_requirement_response::Result::Ok(ok) => ok.requirement,
        get_reboot_requirement_response::Result::Error(error) => {
            panic!("unexpected error {error:?}")
        }
    }
}

#[test]
fn a_required_reboot_travels_as_required() {
    let sent = wire_requirement(Ok(RebootRequirement::Required));

    assert_eq!(sent, WireRequirement::Required as i32);
}

#[test]
fn a_probe_that_found_no_need_travels_as_not_required() {
    let sent = wire_requirement(Ok(RebootRequirement::NotRequired));

    assert_eq!(sent, WireRequirement::NotRequired as i32);
}

/// A reading that was taken is never the wire's zero: `UNSPECIFIED` means "not
/// measured" and a measured answer must not be readable as that.
#[test]
fn a_reading_that_was_taken_is_never_sent_as_unspecified() {
    for requirement in [RebootRequirement::Required, RebootRequirement::NotRequired] {
        let sent = wire_requirement(Ok(requirement));

        assert_ne!(sent, WireRequirement::Unspecified as i32, "{requirement:?}");
    }
}

/// A probe the agent could not run is the error arm, carried unchanged, and
/// never an `ok` saying no reboot is needed.
#[test]
fn a_probe_that_did_not_run_travels_as_the_error_and_never_as_not_required() {
    let error = AgentError {
        code: ErrorCode::SystemFailure as i32,
        message: String::from("the reboot probe could not be run"),
        tool_output: String::new(),
    };

    let result = reported_reboot_requirement(Err(error.clone()));

    assert_eq!(
        result,
        get_reboot_requirement_response::Result::Error(error)
    );
    assert_ne!(
        result,
        get_reboot_requirement_response::Result::Ok(GetRebootRequirementOk {
            requirement: WireRequirement::NotRequired as i32,
        })
    );
}
