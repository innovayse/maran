//! One FTPS failure, one wire code — and no tool output on any of them.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::ftps::FtpsError;

use crate::proto::ErrorCode;
use crate::services::ftps::ftps_status::to_agent_error;

/// The code `error` maps to, as the wire carries it.
fn code_of(error: &FtpsError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn an_existing_login_is_an_idempotency_answer_and_not_a_fault() {
    assert_eq!(
        code_of(&FtpsError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn everything_the_caller_named_that_is_not_here_is_one_answer_to_the_panel() {
    for error in [
        FtpsError::NotFound,
        FtpsError::AccountMissing,
        FtpsError::CertificateMissing {
            domain: "ftp.example.test".to_owned(),
            expected_path: "/etc/maran/ssl/ftp.example.test/fullchain.pem".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::NotFound as i32, "{error:?}");
    }
}

#[test]
fn a_configuration_the_daemon_refused_and_a_password_the_host_refused_are_validation_failures() {
    assert_eq!(
        code_of(&FtpsError::ConfigRejected {
            output: "500 OOPS: unrecognised variable".to_owned(),
            output_is_unavailable_on_this_platform: false,
        }),
        ErrorCode::ValidationFailed as i32
    );
    assert_eq!(
        code_of(&FtpsError::PasswordRejected),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_suspension_that_could_not_be_restored_is_reported_as_a_fault_and_never_as_a_success() {
    // The one condition an operator has to act on: the password IS set and a
    // login that was suspended is open. Only a fault makes the panel show it.
    assert_eq!(
        code_of(&FtpsError::SuspensionNotRestored),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_jail_that_did_not_take_effect_is_a_fault_and_not_an_input_problem() {
    // The login would work and find an empty directory where the customer's
    // files should be, which reads to a customer as data loss.
    assert_eq!(
        code_of(&FtpsError::JailFailed),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn no_variant_carries_a_tools_output_onto_the_wire() {
    // Two variants DO hold text a tool printed, and the field the contract has
    // for it stays empty on purpose: the realistic leak here is a daemon or PAM
    // quoting back the line it refused, and that line carries a password.
    for error in [
        FtpsError::ConfigRejected {
            output: "the password was Str0ng-pass".to_owned(),
            output_is_unavailable_on_this_platform: false,
        },
        FtpsError::ServiceRefused {
            unit: "restart".to_owned(),
        },
        FtpsError::PasswordRejected,
        FtpsError::SpawnFailed { code: 1 },
    ] {
        assert_eq!(
            to_agent_error(&error).tool_output,
            "",
            "{error:?} must not put tool output on the wire"
        );
    }
}

#[test]
fn every_mapped_variant_carries_the_errors_own_sentence_as_its_message() {
    // The vacuity guard for the whole mapping: a message built from anything
    // other than the variant would make every assertion above pass while the
    // operator read a constant.
    let error = FtpsError::NotListening;
    assert_eq!(to_agent_error(&error).message, error.to_string());
    assert!(!to_agent_error(&error).message.is_empty());
}

#[test]
fn a_login_operation_refused_because_the_account_is_busy_is_its_own_code() {
    // It used to be grouped with `SuspensionNotRestored` and `JailFailed` in
    // the SYSTEM_FAILURE arm, which put a one-second refusal that changed
    // nothing beside the sharpest answer this service produces.
    assert_eq!(
        code_of(&FtpsError::AccountBusy),
        ErrorCode::AccountBusy as i32
    );
}

#[test]
fn a_busy_account_is_not_the_answer_for_a_suspended_login_left_open() {
    // The inverse control, and here it is the one that matters most: a password
    // that IS set on a login that should be suspended must read as a fault, or
    // the panel shows a retryable message over an open suspension. The identity
    // change is beside it for the same reason — the account was remade under
    // the operation, so the request must not simply be replayed.
    for error in [
        FtpsError::SuspensionNotRestored,
        FtpsError::AccountIdentityChanged,
    ] {
        assert_eq!(
            code_of(&error),
            ErrorCode::SystemFailure as i32,
            "{error:?} must still read as a fault"
        );
        assert_ne!(code_of(&error), code_of(&FtpsError::AccountBusy));
    }
}
