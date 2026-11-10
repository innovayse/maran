//! A finished database transfer as the terminal message of its stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;
use maran_ops::transfer::model::dump_summary::DumpSummary;

use super::terminal_of_dump;
use crate::proto::{SourceFailureReason, TransferFailureReason, stream_remote_database_response};
use crate::services::transfer::source_failure::SourceFailure;

#[test]
fn a_finished_transfer_is_done_with_the_bytes_loaded() {
    let (terminal, reason, transfer_reason) = terminal_of_dump(Ok(DumpSummary { dump_bytes: 512 }));

    assert!(matches!(
        terminal,
        stream_remote_database_response::Result::Done(done) if done.dump_bytes == 512
    ));
    assert_eq!(reason, SourceFailureReason::Unspecified);
    assert_eq!(transfer_reason, TransferFailureReason::Unspecified);
}

#[test]
fn a_refused_dump_is_an_error_that_says_the_loader_refused_it() {
    let (terminal, _, transfer_reason) =
        terminal_of_dump(Err(SourceFailure::of(&TransferOpError::LoadRefused)));

    assert!(matches!(
        terminal,
        stream_remote_database_response::Result::Error(_)
    ));
    assert_eq!(transfer_reason, TransferFailureReason::LoadRefused);
}

#[test]
fn a_host_key_that_changed_keeps_its_connection_reason() {
    let (_, reason, transfer_reason) =
        terminal_of_dump(Err(SourceFailure::of(&TransferOpError::HostKeyChanged)));

    assert_eq!(reason, SourceFailureReason::HostKeyChanged);
    assert_eq!(transfer_reason, TransferFailureReason::Unspecified);
}
