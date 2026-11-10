//! Which failure of a transfer an error was, for the panel to tell apart.

use maran_ops::transfer::TransferOpError;

use crate::proto::TransferFailureReason;

/// The reason a failed pull or dump is reported with, or unspecified for every
/// other failure.
///
/// A match with a wildcard and not an exhaustive one, on purpose, for the reason
/// `source_failure_reason_of` gives: `TransferOpError` is `#[non_exhaustive]`,
/// and a failure this function does not name is by definition not one of the
/// seven the panel acts on differently (a connection failure has its own reason
/// beside this one).
#[must_use]
pub fn transfer_failure_reason_of(error: &TransferOpError) -> TransferFailureReason {
    match error {
        TransferOpError::DiskInsufficient => TransferFailureReason::DiskInsufficient,
        TransferOpError::PullFailed { .. } => TransferFailureReason::PullFailed,
        TransferOpError::PullIncomplete => TransferFailureReason::PullIncomplete,
        TransferOpError::DumpFailed { .. } => TransferFailureReason::DumpFailed,
        TransferOpError::DumpTooLarge => TransferFailureReason::DumpTooLarge,
        TransferOpError::LoadRefused => TransferFailureReason::LoadRefused,
        TransferOpError::LoadFailed => TransferFailureReason::LoadFailed,
        _ => TransferFailureReason::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/transfer_failure_reason_of_tests.rs"]
mod tests;
