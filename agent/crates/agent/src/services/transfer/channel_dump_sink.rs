//! The [`DumpSink`] that puts a database transfer's messages onto the gRPC stream.

use maran_ops::transfer::model::dump_sink::DumpSink;
use maran_ops::transfer::model::dump_step::DumpStep;
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{
    DatabaseTransferStep, SourceFailureReason, StreamRemoteDatabaseProgress,
    StreamRemoteDatabaseResponse, TransferFailureReason, stream_remote_database_response,
};
use crate::services::wire::deadline_sender::DeadlineSender;

/// Delivers a database transfer's steps into the bounded channel behind the
/// response stream.
///
/// The transfer is "run to completion and reclaim": a step that cannot be
/// delivered because the client left changes nothing about the work, which goes
/// on to its end and cleans up after itself.
pub struct ChannelDumpSink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<StreamRemoteDatabaseResponse, Status>>,
}

impl ChannelDumpSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(messages: Sender<Result<StreamRemoteDatabaseResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(messages),
        }
    }

    /// Delivers the terminal message under the same deadline every other message
    /// gets, with the failure reasons beside it when there are any.
    pub fn terminal(
        &mut self,
        terminal: stream_remote_database_response::Result,
        reason: SourceFailureReason,
        transfer_reason: TransferFailureReason,
    ) {
        let _ = self.sender.send(Ok(StreamRemoteDatabaseResponse {
            result: Some(terminal),
            failure_reason: reason as i32,
            transfer_failure_reason: transfer_reason as i32,
        }));
    }
}

impl DumpSink for ChannelDumpSink {
    /// Sends the step the transfer has reached.
    fn step(&mut self, step: DumpStep) {
        let wire = match step {
            DumpStep::Dumping => DatabaseTransferStep::Dumping,
            DumpStep::Loading => DatabaseTransferStep::Loading,
        };
        let _ = self.sender.send(Ok(StreamRemoteDatabaseResponse {
            result: Some(stream_remote_database_response::Result::Progress(
                StreamRemoteDatabaseProgress { step: wire as i32 },
            )),
            failure_reason: SourceFailureReason::Unspecified as i32,
            transfer_failure_reason: TransferFailureReason::Unspecified as i32,
        }));
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/channel_dump_sink_tests.rs"]
mod tests;
