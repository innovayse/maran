//! Which connection failure a remote-source error was, for the panel to tell apart.

use maran_ops::transfer::TransferOpError;

use crate::proto::SourceFailureReason;

/// The reason a failed call is reported with, or unspecified for a failure of this
/// host.
///
/// A match with a wildcard and not an exhaustive one, on purpose:
/// `TransferOpError` is `#[non_exhaustive]` and holds every failure of the area,
/// and a failure this function does not name is by definition not one of the four
/// the panel acts on differently. A bound, a missing scratch directory or a key
/// that could not be generated is a fault of THIS host and is never reported as
/// something the operator should fix on the source.
#[must_use]
pub fn source_failure_reason_of(error: &TransferOpError) -> SourceFailureReason {
    match error {
        TransferOpError::HostKeyChanged => SourceFailureReason::HostKeyChanged,
        TransferOpError::AuthRefused => SourceFailureReason::AuthRefused,
        TransferOpError::Unreachable => SourceFailureReason::Unreachable,
        TransferOpError::AddressRefused => SourceFailureReason::AddressRefused,
        _ => SourceFailureReason::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/source_failure_reason_of_tests.rs"]
mod tests;
