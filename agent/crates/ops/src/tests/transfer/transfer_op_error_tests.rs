//! Tests for [`TransferOpError`]: every variant has text, none can carry a
//! secret, and a spawn bound stays a bound.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use super::TransferOpError;

/// One of every variant.
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
            program: "/usr/bin/ssh".to_owned(),
            deadline: Duration::from_secs(900),
        }),
        TransferOpError::Spawn(SpawnError::Start {
            program: "/usr/bin/ssh".to_owned(),
            reason: "not found".to_owned(),
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
fn every_variant_has_a_distinct_non_empty_message() {
    let messages: Vec<String> = every_variant().iter().map(ToString::to_string).collect();

    for message in &messages {
        assert!(!message.is_empty());
    }
    let mut sorted = messages.clone();
    sorted.sort();
    sorted.dedup();
    assert_eq!(sorted.len(), messages.len(), "two variants share a message");
}

#[test]
fn a_spawn_bound_becomes_the_output_bound_variant() {
    let error = TransferOpError::from(SpawnError::Bound(BoundExceeded::TimedOut {
        program: "/usr/bin/ssh".to_owned(),
        deadline: Duration::from_secs(1),
    }));

    assert!(matches!(error, TransferOpError::OutputBound(_)));
}

#[test]
fn any_other_spawn_error_becomes_the_spawn_variant() {
    let error = TransferOpError::from(SpawnError::Input {
        program: "/usr/bin/ssh".to_owned(),
    });

    assert!(matches!(error, TransferOpError::Spawn(_)));
}

#[test]
fn the_connection_errors_name_no_host_no_path_and_no_argument() {
    for error in [
        TransferOpError::AddressRefused,
        TransferOpError::HostKeyChanged,
        TransferOpError::AuthRefused,
        TransferOpError::Unreachable,
        TransferOpError::InvalidRemoteOutput,
    ] {
        let text = format!("{error} {error:?}");

        assert!(!text.contains('/'), "{text}");
        assert!(!text.contains("ssh-"), "{text}");
    }
}
