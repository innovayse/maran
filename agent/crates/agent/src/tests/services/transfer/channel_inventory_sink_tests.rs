//! Tests for the sink that puts an inventory read onto the response stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::transfer::inventory::model::inventory_sink::InventorySink;
use maran_ops::transfer::inventory::model::inventory_step::InventoryStep;
use maran_ops::transfer::inventory::model::source_account_record::SourceAccountRecord;
use maran_ops::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use maran_ops::transfer::inventory::model::source_warning_record::SourceWarningRecord;
use tokio::sync::mpsc;

use super::ChannelInventorySink;
use crate::proto::{SourceFailureReason, SourceInventoryStep, read_source_inventory_response};

fn account() -> SourceAccountRecord {
    SourceAccountRecord {
        name: AccountName::parse("alice").unwrap(),
        home: "/home/alice".to_owned(),
        disk_bytes: None,
        sites: Vec::new(),
        databases: Vec::new(),
        cron_entry_count: 0,
        mailbox_count: 0,
    }
}

#[test]
fn progress_accounts_and_warnings_arrive_in_the_order_they_were_given() {
    let (sender, mut receiver) = mpsc::channel(8);
    let mut sink = ChannelInventorySink::new(sender);

    sink.progress(InventoryStep::Accounts);
    sink.account(account());
    sink.warning(SourceWarningRecord::about_nothing(
        SourceWarningKind::MailNotMigrated,
    ));

    let results: Vec<_> = (0..3)
        .map(|_| receiver.try_recv().unwrap().unwrap().result.unwrap())
        .collect();
    assert!(matches!(
        &results[0],
        read_source_inventory_response::Result::Progress(progress)
            if progress.step == SourceInventoryStep::Accounts as i32
    ));
    assert!(matches!(
        &results[1],
        read_source_inventory_response::Result::Account(found) if found.name == "alice"
    ));
    assert!(matches!(
        &results[2],
        read_source_inventory_response::Result::Warning(_)
    ));
    assert!(sink.is_open());
}

#[test]
fn a_client_that_left_closes_the_sink_and_nothing_more_is_sent() {
    let (sender, receiver) = mpsc::channel(8);
    let mut sink = ChannelInventorySink::new(sender);
    assert!(
        sink.is_open(),
        "inverse control: a connected client is open"
    );

    drop(receiver);
    assert!(
        !sink.is_open(),
        "the channel closing is noticed with nothing to send"
    );

    sink.progress(InventoryStep::Sites);
    sink.account(account());
    assert!(!sink.is_open());
}

#[test]
fn the_terminal_message_carries_the_reason_beside_it() {
    let (sender, mut receiver) = mpsc::channel(8);
    let mut sink = ChannelInventorySink::new(sender);

    sink.terminal(
        read_source_inventory_response::Result::Done(crate::proto::ReadSourceInventoryDone {
            account_count: 1,
            warning_count: 0,
        }),
        SourceFailureReason::Unreachable,
    );

    let message = receiver.try_recv().unwrap().unwrap();
    assert!(matches!(
        message.result,
        Some(read_source_inventory_response::Result::Done(_))
    ));
    assert_eq!(
        message.failure_reason,
        SourceFailureReason::Unreachable as i32
    );
}
