//! Tests for the wire code every remote-source failure travels as.
//!
//! Every variant of `TransferOpError` appears here (rules/testing.md), organised
//! by the distinction the panel acts on: a request the agent will not act on, a
//! refusal that left both hosts as they were, and a fault of the exchange or of
//! this machine. The second proposition is secrecy: `tool_output` is empty for
//! every variant, because no variant can carry what the source printed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_ops::transfer::TransferOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// A variant of every kind, once, so the exhaustiveness of this file is a fact a
/// reader can check rather than a claim.
fn every_variant() -> Vec<TransferOpError> {
    vec![
        TransferOpError::AddressRefused,
        TransferOpError::HostKeyChanged,
        TransferOpError::AuthRefused,
        TransferOpError::Unreachable,
        TransferOpError::KeyGenerationFailed,
        TransferOpError::ScratchUnavailable,
        TransferOpError::HostAddressesUnavailable,
        TransferOpError::OutputBound(BoundExceeded::TimedOut {
            program: "ssh".to_owned(),
            deadline: Duration::from_secs(1),
        }),
        TransferOpError::Spawn(SpawnError::Start {
            program: "ssh".to_owned(),
            reason: "missing".to_owned(),
        }),
        TransferOpError::InvalidRemoteOutput,
        TransferOpError::DiskInsufficient,
        TransferOpError::PullFailed { status: 23 },
        TransferOpError::PullIncomplete,
        TransferOpError::DumpFailed { status: 2 },
        TransferOpError::DumpTooLarge,
        TransferOpError::LoadRefused,
        TransferOpError::LoadFailed,
        TransferOpError::Cancelled,
    ]
}

#[test]
fn the_transfer_failures_map_to_the_codes_the_panel_acts_on() {
    let table = [
        (
            TransferOpError::DiskInsufficient,
            ErrorCode::BelowFreeSpaceFloor,
        ),
        (TransferOpError::DumpTooLarge, ErrorCode::TooLarge),
        (TransferOpError::LoadRefused, ErrorCode::ValidationFailed),
        (
            TransferOpError::PullFailed { status: 23 },
            ErrorCode::SystemFailure,
        ),
        (TransferOpError::PullIncomplete, ErrorCode::SystemFailure),
        (
            TransferOpError::DumpFailed { status: 2 },
            ErrorCode::SystemFailure,
        ),
        (TransferOpError::LoadFailed, ErrorCode::SystemFailure),
        (TransferOpError::Cancelled, ErrorCode::SystemFailure),
    ];

    for (failure, code) in table {
        assert_eq!(to_agent_error(&failure).code, code as i32, "{failure}");
    }
}

#[test]
fn an_address_the_agent_never_connects_to_is_invalid_input() {
    let error = to_agent_error(&TransferOpError::AddressRefused);

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_changed_host_key_and_a_refused_key_are_refusals_that_changed_nothing() {
    for failure in [
        TransferOpError::HostKeyChanged,
        TransferOpError::AuthRefused,
    ] {
        assert_eq!(
            to_agent_error(&failure).code,
            ErrorCode::ValidationFailed as i32,
            "{failure}"
        );
    }
}

#[test]
fn a_silent_source_a_bound_and_the_faults_of_this_host_are_system_failures() {
    for failure in [
        TransferOpError::Unreachable,
        TransferOpError::KeyGenerationFailed,
        TransferOpError::ScratchUnavailable,
        TransferOpError::HostAddressesUnavailable,
        TransferOpError::OutputBound(BoundExceeded::OutputTooLarge {
            program: "ssh".to_owned(),
            stream: maran_agent_core::utils::output_stream::OutputStream::Stdout,
            cap: 1,
        }),
        TransferOpError::Spawn(SpawnError::Wait {
            program: "ssh".to_owned(),
            reason: "gone".to_owned(),
        }),
        TransferOpError::InvalidRemoteOutput,
    ] {
        assert_eq!(
            to_agent_error(&failure).code,
            ErrorCode::SystemFailure as i32,
            "{failure}"
        );
    }
}

#[test]
fn no_variant_puts_tool_output_on_the_wire() {
    for failure in every_variant() {
        assert!(to_agent_error(&failure).tool_output.is_empty(), "{failure}");
    }
}

#[test]
fn every_variant_has_an_operator_message() {
    for failure in every_variant() {
        assert!(!to_agent_error(&failure).message.is_empty(), "{failure:?}");
    }
}

#[test]
fn the_two_refusals_are_not_system_failures_and_the_inverse_holds() {
    // Inverse control: a mapping that answered one code for everything would pass
    // every test above that only looks at one group.
    let refused = to_agent_error(&TransferOpError::HostKeyChanged).code;
    let fault = to_agent_error(&TransferOpError::ScratchUnavailable).code;

    assert_ne!(refused, fault);
}
