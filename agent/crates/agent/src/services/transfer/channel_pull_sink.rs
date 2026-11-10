//! The [`PullSink`] that puts a pull's messages onto the gRPC stream.

use maran_ops::transfer::model::pull_sink::PullSink;
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{
    PullAccountFilesProgress, PullAccountFilesResponse, SourceFailureReason, TransferFailureReason,
    pull_account_files_response,
};
use crate::services::wire::deadline_sender::DeadlineSender;

/// Delivers a pull's progress into the bounded channel behind the response
/// stream.
///
/// The bound (a full channel makes the send wait, a closed one fails it, one that
/// stays full past the patience fails it too) lives in [`DeadlineSender`]. The
/// pull is cancellation class "stop": a failed send is remembered,
/// [`PullSink::is_open`] answers false from then on, and the pull ends at its next
/// slice boundary without asking the source for anything more.
pub struct ChannelPullSink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<PullAccountFilesResponse, Status>>,
    /// Whether a send has failed, which means the client is gone or stalled.
    stopped: bool,
}

impl ChannelPullSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(messages: Sender<Result<PullAccountFilesResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(messages),
            stopped: false,
        }
    }

    /// Delivers the terminal message under the same deadline every other message
    /// gets, with the failure reasons beside it when there are any. The outcome
    /// is discarded; what the bound buys is that the thread comes back.
    pub fn terminal(
        &mut self,
        terminal: pull_account_files_response::Result,
        reason: SourceFailureReason,
        transfer_reason: TransferFailureReason,
    ) {
        let _ = self.sender.send(Ok(PullAccountFilesResponse {
            result: Some(terminal),
            failure_reason: reason as i32,
            transfer_failure_reason: transfer_reason as i32,
        }));
    }
}

impl PullSink for ChannelPullSink {
    /// Sends the count of runs that have ended and remembers a failed send.
    fn progress(&mut self, slices_completed: u32) {
        if self.stopped {
            return;
        }
        let sent = self.sender.send(Ok(PullAccountFilesResponse {
            result: Some(pull_account_files_response::Result::Progress(
                PullAccountFilesProgress { slices_completed },
            )),
            failure_reason: SourceFailureReason::Unspecified as i32,
            transfer_failure_reason: TransferFailureReason::Unspecified as i32,
        }));
        if sent.is_err() {
            self.stopped = true;
        }
    }

    /// Whether the client is still there: no send has failed and the channel is
    /// open.
    fn is_open(&self) -> bool {
        !self.stopped && self.sender.is_open()
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/channel_pull_sink_tests.rs"]
mod tests;
