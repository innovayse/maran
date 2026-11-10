//! Tests for the sink that puts a database transfer onto the response stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::model::dump_sink::DumpSink;
use maran_ops::transfer::model::dump_step::DumpStep;
use tokio::sync::mpsc;

use super::ChannelDumpSink;
use crate::proto::{DatabaseTransferStep, stream_remote_database_response};

#[test]
fn the_steps_arrive_in_the_order_they_were_reached() {
    let (sender, mut receiver) = mpsc::channel(8);
    let mut sink = ChannelDumpSink::new(sender);

    sink.step(DumpStep::Dumping);
    sink.step(DumpStep::Loading);

    let steps: Vec<_> = (0..2)
        .map(
            |_| match receiver.try_recv().unwrap().unwrap().result.unwrap() {
                stream_remote_database_response::Result::Progress(progress) => progress.step,
                other => panic!("not a progress message: {other:?}"),
            },
        )
        .collect();
    assert_eq!(
        steps,
        vec![
            DatabaseTransferStep::Dumping as i32,
            DatabaseTransferStep::Loading as i32
        ]
    );
}

#[test]
fn a_client_that_left_does_not_make_a_step_panic() {
    let (sender, receiver) = mpsc::channel(8);
    let mut sink = ChannelDumpSink::new(sender);
    drop(receiver);

    // Run to completion: the work goes on, and a step nobody hears is dropped.
    sink.step(DumpStep::Dumping);
}
