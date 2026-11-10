//! Tests for the terminal message of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;
use maran_ops::transfer::inventory::model::inventory_summary::InventorySummary;

use super::terminal_of_inventory;
use crate::proto::{SourceFailureReason, read_source_inventory_response};
use crate::services::transfer::source_failure::SourceFailure;

#[test]
fn a_complete_read_ends_with_its_counts_and_no_reason() {
    let (terminal, reason) = terminal_of_inventory(Ok(InventorySummary {
        account_count: 7,
        warning_count: 2,
    }));

    let read_source_inventory_response::Result::Done(done) = terminal else {
        panic!("a complete read ends with done");
    };
    assert_eq!((done.account_count, done.warning_count), (7, 2));
    assert_eq!(reason, SourceFailureReason::Unspecified);
}

#[test]
fn a_failed_read_ends_with_the_error_and_the_reason_beside_it() {
    let (terminal, reason) =
        terminal_of_inventory(Err(SourceFailure::of(&TransferOpError::HostKeyChanged)));

    assert!(matches!(
        terminal,
        read_source_inventory_response::Result::Error(_)
    ));
    assert_eq!(reason, SourceFailureReason::HostKeyChanged);
}

#[test]
fn a_fault_of_this_host_ends_with_the_error_and_no_reason() {
    let (terminal, reason) = terminal_of_inventory(Err(SourceFailure::of(
        &TransferOpError::KeyGenerationFailed,
    )));

    assert!(matches!(
        terminal,
        read_source_inventory_response::Result::Error(_)
    ));
    assert_eq!(reason, SourceFailureReason::Unspecified);
}
