//! Which failure of a transfer the panel is told it was.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;

use super::transfer_failure_reason_of;
use crate::proto::TransferFailureReason;

#[test]
fn each_failure_of_a_transfer_has_its_own_reason() {
    let table = [
        (
            TransferOpError::DiskInsufficient,
            TransferFailureReason::DiskInsufficient,
        ),
        (
            TransferOpError::PullFailed { status: 23 },
            TransferFailureReason::PullFailed,
        ),
        (
            TransferOpError::PullIncomplete,
            TransferFailureReason::PullIncomplete,
        ),
        (
            TransferOpError::DumpFailed { status: 2 },
            TransferFailureReason::DumpFailed,
        ),
        (
            TransferOpError::DumpTooLarge,
            TransferFailureReason::DumpTooLarge,
        ),
        (
            TransferOpError::LoadRefused,
            TransferFailureReason::LoadRefused,
        ),
        (
            TransferOpError::LoadFailed,
            TransferFailureReason::LoadFailed,
        ),
    ];

    for (error, reason) in table {
        assert_eq!(transfer_failure_reason_of(&error), reason, "{error}");
    }
}

#[test]
fn a_connection_failure_and_a_fault_of_this_host_carry_no_transfer_reason() {
    // The inverse control: the reasons above are not handed to everything.
    for error in [
        TransferOpError::HostKeyChanged,
        TransferOpError::AuthRefused,
        TransferOpError::Unreachable,
        TransferOpError::ScratchUnavailable,
        TransferOpError::Cancelled,
    ] {
        assert_eq!(
            transfer_failure_reason_of(&error),
            TransferFailureReason::Unspecified,
            "{error}"
        );
    }
}
