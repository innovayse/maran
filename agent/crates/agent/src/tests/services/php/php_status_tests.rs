//! Tests for the wire code every PHP failure travels as.
//!
//! One assertion per variant of `PhpOpError`, because the codes are a
//! CONTRACT — `php.proto` names them to callers — and because
//! `rules/testing.md` requires every typed error variant to appear in at least
//! one test. Before this file four of the thirteen variants appeared nowhere
//! in the suite.
//!
//! The distinction this file exists to pin is the one an operator acts on: a
//! request the agent will not write (INVALID_INPUT), a version the host does
//! not have (VALIDATION_FAILED), and a machine that would not do as it was
//! told (SYSTEM_FAILURE). Collapsing any pair of those sends the wrong person
//! looking.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::php::PhpOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// The code `error` is reported as.
fn code_of(error: &PhpOpError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn everything_the_caller_asked_for_that_the_agent_will_not_write_is_invalid_input() {
    for error in [
        PhpOpError::UnsupportedVersion {
            version: "5.6".to_owned(),
        },
        PhpOpError::OverrideNotAllowed {
            name: "disable_functions".to_owned(),
        },
        PhpOpError::OverrideMalformed {
            name: "memory_limit".to_owned(),
            value: "lots".to_owned(),
        },
        PhpOpError::OverrideOutOfRange {
            name: "memory_limit".to_owned(),
            value: "64G".to_owned(),
            maximum: 536_870_912,
        },
        PhpOpError::OverrideControlCharacter {
            name: "upload_max_filesize".to_owned(),
        },
        PhpOpError::WorkerBudgetOutOfRange {
            requested: 900,
            minimum: 1,
            maximum: 20,
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_version_that_is_not_installed_is_a_validation_failure_and_never_invalid_input() {
    // The two are deliberately different answers: an unsupported version is
    // one this agent will never install, while an uninstalled one is a version
    // this host merely does not have yet.
    assert_eq!(
        code_of(&PhpOpError::PhpVersionNotInstalled {
            version: "8.4".to_owned(),
        }),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_pool_the_validator_refused_is_a_validation_failure_because_the_previous_pool_is_back() {
    assert_eq!(
        code_of(&PhpOpError::PoolValidation {
            stderr: "ERROR: failed to post process the configuration".to_owned(),
        }),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn every_fault_of_this_machine_is_a_system_failure() {
    for error in [
        PhpOpError::PackageManager {
            stderr: "E: Unable to locate package php8.4-fpm".to_owned(),
        },
        PhpOpError::ServiceEnable {
            stderr: "Failed to enable unit".to_owned(),
        },
        PhpOpError::ReloadFailed {
            stderr: "job for php8.3-fpm.service failed".to_owned(),
        },
        PhpOpError::Render {
            reason: "template not found".to_owned(),
        },
        PhpOpError::ConfigWrite {
            reason: "no space left on device".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    }
}

#[test]
fn only_the_failures_that_ran_a_tool_carry_that_tools_output() {
    for (error, expected) in [
        (
            PhpOpError::PoolValidation {
                stderr: "ERROR: failed to post process".to_owned(),
            },
            "ERROR: failed to post process",
        ),
        (
            PhpOpError::PackageManager {
                stderr: "E: Unable to locate package".to_owned(),
            },
            "E: Unable to locate package",
        ),
        (
            PhpOpError::ServiceEnable {
                stderr: "Failed to enable unit".to_owned(),
            },
            "Failed to enable unit",
        ),
        (
            PhpOpError::ReloadFailed {
                stderr: "job for php8.3-fpm.service failed".to_owned(),
            },
            "job for php8.3-fpm.service failed",
        ),
    ] {
        assert_eq!(to_agent_error(&error).tool_output, expected);
    }
}

#[test]
fn a_refusal_of_the_callers_own_input_carries_no_tool_output_at_all() {
    for error in [
        PhpOpError::UnsupportedVersion {
            version: "5.6".to_owned(),
        },
        PhpOpError::OverrideNotAllowed {
            name: "disable_functions".to_owned(),
        },
        PhpOpError::OverrideMalformed {
            name: "memory_limit".to_owned(),
            value: "lots".to_owned(),
        },
        PhpOpError::OverrideOutOfRange {
            name: "memory_limit".to_owned(),
            value: "64G".to_owned(),
            maximum: 536_870_912,
        },
        PhpOpError::OverrideControlCharacter {
            name: "upload_max_filesize".to_owned(),
        },
        PhpOpError::WorkerBudgetOutOfRange {
            requested: 900,
            minimum: 1,
            maximum: 20,
        },
        PhpOpError::PhpVersionNotInstalled {
            version: "8.4".to_owned(),
        },
        PhpOpError::Render {
            reason: "template not found".to_owned(),
        },
        PhpOpError::ConfigWrite {
            reason: "no space left on device".to_owned(),
        },
    ] {
        assert!(to_agent_error(&error).tool_output.is_empty());
    }
}

#[test]
fn the_message_is_the_failures_own_display_and_never_a_sentence_invented_here() {
    let error = PhpOpError::UnsupportedVersion {
        version: "5.6".to_owned(),
    };

    assert_eq!(to_agent_error(&error).message, error.to_string());
}
