//! Tests for which connection failure a remote-source error is reported as.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_ops::transfer::TransferOpError;

use super::source_failure_reason_of;
use crate::proto::SourceFailureReason;

#[test]
fn each_connection_failure_the_panel_tells_apart_has_its_own_reason() {
    assert_eq!(
        source_failure_reason_of(&TransferOpError::HostKeyChanged),
        SourceFailureReason::HostKeyChanged
    );
    assert_eq!(
        source_failure_reason_of(&TransferOpError::AuthRefused),
        SourceFailureReason::AuthRefused
    );
    assert_eq!(
        source_failure_reason_of(&TransferOpError::Unreachable),
        SourceFailureReason::Unreachable
    );
    assert_eq!(
        source_failure_reason_of(&TransferOpError::AddressRefused),
        SourceFailureReason::AddressRefused
    );
}

#[test]
fn a_fault_of_this_host_is_never_blamed_on_the_source() {
    for failure in [
        TransferOpError::KeyGenerationFailed,
        TransferOpError::ScratchUnavailable,
        TransferOpError::HostAddressesUnavailable(HostAddressesError::Ipv4Missing),
        TransferOpError::InvalidRemoteOutput,
        TransferOpError::OutputBound(BoundExceeded::TimedOut {
            program: "ssh".to_owned(),
            deadline: Duration::from_secs(1),
        }),
    ] {
        assert_eq!(
            source_failure_reason_of(&failure),
            SourceFailureReason::Unspecified,
            "{failure}"
        );
    }
}
