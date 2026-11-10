//! A finished pull as the terminal message of its stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;
use maran_ops::transfer::model::pull_summary::PullSummary;

use super::terminal_of_pull;
use crate::proto::{SourceFailureReason, TransferFailureReason, pull_account_files_response};
use crate::services::transfer::source_failure::SourceFailure;

#[test]
fn a_finished_pull_is_done_with_the_measured_size_and_no_reason() {
    let (terminal, reason, transfer_reason) = terminal_of_pull(Ok(PullSummary {
        slices: 4,
        source_bytes: Some(2048),
    }));

    assert!(matches!(
        terminal,
        pull_account_files_response::Result::Done(done)
            if done.slices == 4 && done.source_bytes == 2048 && done.source_bytes_known
    ));
    assert_eq!(reason, SourceFailureReason::Unspecified);
    assert_eq!(transfer_reason, TransferFailureReason::Unspecified);
}

#[test]
fn an_unmeasured_source_is_not_reported_as_empty() {
    let (terminal, _, _) = terminal_of_pull(Ok(PullSummary {
        slices: 1,
        source_bytes: None,
    }));

    assert!(matches!(
        terminal,
        pull_account_files_response::Result::Done(done) if !done.source_bytes_known
    ));
}

#[test]
fn a_failed_pull_is_an_error_with_both_reasons() {
    let failure = SourceFailure::of(&TransferOpError::DiskInsufficient);

    let (terminal, reason, transfer_reason) = terminal_of_pull(Err(failure));

    assert!(matches!(
        terminal,
        pull_account_files_response::Result::Error(_)
    ));
    assert_eq!(reason, SourceFailureReason::Unspecified);
    assert_eq!(transfer_reason, TransferFailureReason::DiskInsufficient);
}
