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
        PhpOpError::OverridePostSizeBelowUpload {
            post_max_size: 8_388_608,
            upload_max_filesize: 33_554_432,
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
fn a_package_source_that_refused_the_install_is_its_own_code_and_not_a_system_failure() {
    let error = PhpOpError::PackageSourceRefused {
        stderr: "Job for maran-php-install@php8.3-fpm.service failed".to_owned(),
    };

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::PackageSourceRefused as i32);
    assert_eq!(
        wire.tool_output,
        "Job for maran-php-install@php8.3-fpm.service failed"
    );
}

#[test]
fn a_package_source_nobody_reached_is_its_own_code_and_not_a_refusal() {
    let error = PhpOpError::PackageSourceUnreachable {
        stderr: "Job for maran-php-install@php8.3-fpm.service failed".to_owned(),
    };

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::PackageSourceUnreachable as i32);
    assert_eq!(
        wire.tool_output,
        "Job for maran-php-install@php8.3-fpm.service failed"
    );
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

#[test]
fn a_pool_write_refused_because_the_account_is_busy_is_its_own_code() {
    // It used to fall through the mapping's `_` arm onto SYSTEM_FAILURE. The
    // refusal happens before the pool file is rendered, staged or validated, so
    // nothing changed and no tool ran to leave an output behind.
    let wire = to_agent_error(&PhpOpError::AccountBusy {
        username: "acme".to_owned(),
    });

    assert_eq!(wire.code, ErrorCode::AccountBusy as i32);
    assert_eq!(wire.tool_output, "");
}

#[test]
fn a_pool_that_is_absent_is_not_found_and_carries_no_tool_output() {
    // `ApplyPhpPoolSettings` never creates a pool, so an account with none is
    // an answer the panel acts on (its pool is created with its site), not a
    // fault of this machine. No tool ran.
    let wire = to_agent_error(&PhpOpError::PoolMissing {
        username: "acme".to_owned(),
        version: "8.3".to_owned(),
    });

    assert_eq!(wire.code, ErrorCode::NotFound as i32);
    assert_eq!(wire.tool_output, "");
}

#[test]
fn a_missing_pool_and_a_version_the_host_lacks_are_different_codes() {
    // The inverse control: both are "something is not there", and only the
    // pool is NOT_FOUND.
    assert_ne!(
        code_of(&PhpOpError::PoolMissing {
            username: "acme".to_owned(),
            version: "8.3".to_owned(),
        }),
        code_of(&PhpOpError::PhpVersionNotInstalled {
            version: "8.3".to_owned(),
        })
    );
}

#[test]
fn a_busy_account_and_a_reload_this_host_refused_are_different_codes() {
    // The inverse control. A php-fpm that would not reload must still be a
    // fault carrying the tool's own words for the operator.
    let fault = PhpOpError::ReloadFailed {
        stderr: "job for php8.3-fpm.service failed".to_owned(),
    };

    assert_eq!(code_of(&fault), ErrorCode::SystemFailure as i32);
    assert_ne!(
        code_of(&fault),
        code_of(&PhpOpError::AccountBusy {
            username: "acme".to_owned(),
        })
    );
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/php/php_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> PhpOpError {
    PhpOpError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> PhpOpError {
    PhpOpError::Bound(BoundExceeded::OutputTooLarge {
        program: "/usr/sbin/stand-in".to_owned(),
        stream: OutputStream::Stdout,
        cap: 4_194_304,
    })
}

/// A deadline is a fault of the host: a system failure, no tool text, and a
/// message that names the program that was killed.
#[test]
fn a_timeout_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&timed_out());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// An output cap is the same kind of fault as a deadline.
#[test]
fn an_output_overflow_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&output_too_large());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// The wildcard arm answers `SystemFailure` too, so deleting the explicit arm
/// changes no behaviour and the two tests above cannot notice it. This one reads
/// the source: the bound must be named, and named before the wildcard.
#[test]
fn the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard() {
    let code_lines: Vec<&str> = MAPPING_SOURCE
        .lines()
        .map(str::trim_start)
        .filter(|line| !line.starts_with("//"))
        .collect();
    let wildcard = code_lines.iter().position(|line| line.starts_with("_ =>"));
    let bound = code_lines
        .iter()
        .position(|line| line.starts_with("PhpOpError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}

/// A name the family has no package for, and a switch-off it has no switch for,
/// are refusals of what the caller asked: INVALID_INPUT with no tool output.
#[test]
fn an_extension_the_host_cannot_install_or_switch_off_is_invalid_input_without_tool_output() {
    for error in [
        PhpOpError::ExtensionNotPackaged {
            extension: "curl".to_owned(),
            version: "8.3".to_owned(),
        },
        PhpOpError::ExtensionSwitchUnsupported {
            extension: "gd".to_owned(),
        },
    ] {
        let wire = to_agent_error(&error);
        assert_eq!(wire.code, ErrorCode::InvalidInput as i32);
        assert!(wire.tool_output.is_empty());
    }
}

/// A switch or probe that failed is a system failure carrying its stderr.
#[test]
fn a_failed_extension_switch_or_probe_is_a_system_failure_carrying_its_stderr() {
    for error in [
        PhpOpError::ExtensionSwitchFailed {
            stderr: "phpdismod: no such module".to_owned(),
        },
        PhpOpError::ExtensionProbeFailed {
            stderr: "php-fpm: cannot load".to_owned(),
        },
    ] {
        let expected = match &error {
            PhpOpError::ExtensionSwitchFailed { stderr }
            | PhpOpError::ExtensionProbeFailed { stderr } => stderr.clone(),
            other => panic!("unexpected {other:?}"),
        };
        let wire = to_agent_error(&error);
        assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
        assert_eq!(wire.tool_output, expected);
    }
}

/// An extension still in the wrong state after a clean switch is a system
/// failure with no tool output, and not an input refusal (inverse control to
/// the refusals above).
#[test]
fn an_extension_left_in_the_wrong_state_is_a_system_failure_without_tool_output() {
    for error in [
        PhpOpError::ExtensionNotLoaded {
            extension: "gd".to_owned(),
        },
        PhpOpError::ExtensionStillLoaded {
            extension: "gd".to_owned(),
        },
    ] {
        let wire = to_agent_error(&error);
        assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
        assert!(wire.tool_output.is_empty());
    }
}

#[test]
fn a_package_no_source_carries_is_its_own_code_and_names_the_package() {
    let error = PhpOpError::PackageNotAvailable {
        package: "php8.2-fpm".to_owned(),
        stderr: "Job for maran-php-install@php8.2-fpm.service failed".to_owned(),
    };

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::PackageNotAvailable as i32);
    assert!(wire.message.contains("php8.2-fpm"), "{}", wire.message);
    assert_eq!(
        wire.tool_output,
        "Job for maran-php-install@php8.2-fpm.service failed"
    );
}

#[test]
fn a_package_no_source_carries_is_not_a_system_failure_nor_a_source_fault() {
    // Inverse control: the three neighbours keep their own codes.
    let local = PhpOpError::PackageManager {
        stderr: "dpkg was interrupted".to_owned(),
    };

    assert_eq!(code_of(&local), ErrorCode::SystemFailure as i32);
    assert_ne!(
        ErrorCode::PackageNotAvailable as i32,
        ErrorCode::PackageSourceRefused as i32
    );
    assert_ne!(
        ErrorCode::PackageNotAvailable as i32,
        ErrorCode::PackageSourceUnreachable as i32
    );
}

#[test]
fn a_package_catalogue_that_could_not_be_read_is_a_system_failure_with_no_tool_output() {
    let error = PhpOpError::PackageCatalogueUnavailable {
        reason: "the package manager's query exited with status 100".to_owned(),
    };

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
}
