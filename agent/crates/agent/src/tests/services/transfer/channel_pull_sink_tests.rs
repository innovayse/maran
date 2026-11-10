//! Tests for the sink that puts a pull onto the response stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::model::pull_sink::PullSink;
use tokio::sync::mpsc;

use super::ChannelPullSink;
use crate::proto::{SourceFailureReason, TransferFailureReason, pull_account_files_response};

#[test]
fn progress_arrives_with_the_count_it_was_given() {
    let (sender, mut receiver) = mpsc::channel(8);
    let mut sink = ChannelPullSink::new(sender);

    sink.progress(3);

    let message = receiver.try_recv().unwrap().unwrap();
    assert!(matches!(
        message.result,
        Some(pull_account_files_response::Result::Progress(progress)) if progress.slices_completed == 3
    ));
    assert!(sink.is_open());
}

#[test]
fn a_client_that_left_closes_the_sink_and_nothing_more_is_sent() {
    let (sender, receiver) = mpsc::channel(8);
    let mut sink = ChannelPullSink::new(sender);
    assert!(
        sink.is_open(),
        "inverse control: a connected client is open"
    );

    drop(receiver);
    sink.progress(1);

    assert!(!sink.is_open());
}

#[test]
fn the_terminal_carries_both_reasons() {
    let (sender, mut receiver) = mpsc::channel(8);
    let mut sink = ChannelPullSink::new(sender);

    sink.terminal(
        pull_account_files_response::Result::Done(crate::proto::PullAccountFilesDone::default()),
        SourceFailureReason::Unreachable,
        TransferFailureReason::PullFailed,
    );

    let message = receiver.try_recv().unwrap().unwrap();
    assert_eq!(
        message.failure_reason,
        SourceFailureReason::Unreachable as i32
    );
    assert_eq!(
        message.transfer_failure_reason,
        TransferFailureReason::PullFailed as i32
    );
}
