//! A finished database transfer wrapped into the terminal message of its stream.

use maran_ops::transfer::model::dump_summary::DumpSummary;

use crate::proto::{
    SourceFailureReason, StreamRemoteDatabaseDone, TransferFailureReason,
    stream_remote_database_response,
};
use crate::services::transfer::source_failure::SourceFailure;

/// Wraps the result of a database transfer into the terminal arm of the response
/// and the two reasons that travel beside it.
#[must_use]
pub fn terminal_of_dump(
    result: Result<DumpSummary, SourceFailure>,
) -> (
    stream_remote_database_response::Result,
    SourceFailureReason,
    TransferFailureReason,
) {
    match result {
        Ok(summary) => (
            stream_remote_database_response::Result::Done(StreamRemoteDatabaseDone {
                dump_bytes: summary.dump_bytes,
            }),
            SourceFailureReason::Unspecified,
            TransferFailureReason::Unspecified,
        ),
        Err(failure) => (
            stream_remote_database_response::Result::Error(failure.error),
            failure.reason,
            failure.transfer_reason,
        ),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/terminal_of_dump_tests.rs"]
mod tests;
