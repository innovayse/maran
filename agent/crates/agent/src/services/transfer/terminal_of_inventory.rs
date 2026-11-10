//! A finished inventory read wrapped into the terminal message of its stream.

use maran_ops::transfer::inventory::model::inventory_summary::InventorySummary;

use crate::proto::{ReadSourceInventoryDone, SourceFailureReason, read_source_inventory_response};
use crate::services::transfer::source_failure::SourceFailure;

/// Wraps the result of an inventory read into the terminal arm of the response
/// and the connection reason that travels beside it.
///
/// The reason is unspecified for a read that completed and for a failure of this
/// host; it is set only together with the error.
#[must_use]
pub fn terminal_of_inventory(
    result: Result<InventorySummary, SourceFailure>,
) -> (read_source_inventory_response::Result, SourceFailureReason) {
    match result {
        Ok(summary) => (
            read_source_inventory_response::Result::Done(ReadSourceInventoryDone {
                account_count: summary.account_count,
                warning_count: summary.warning_count,
            }),
            SourceFailureReason::Unspecified,
        ),
        Err(failure) => (
            read_source_inventory_response::Result::Error(failure.error),
            failure.reason,
        ),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/terminal_of_inventory_tests.rs"]
mod tests;
