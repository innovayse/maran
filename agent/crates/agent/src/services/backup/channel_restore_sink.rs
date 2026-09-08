//! The [`RestoreSink`] that puts a restore's progress onto the gRPC stream.

use maran_ops::backup::{RestoreSink, RestoreStage};
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{Progress, RestoreBackupResponse, restore_backup_response};

/// Delivers a restore's progress into the bounded channel behind the response
/// stream.
///
/// A second sink beside [`ChannelProgressSink`](super::channel_progress_sink::ChannelProgressSink)
/// and not one generic over the stage, because the two operations report
/// different work into different response types — the same reason
/// [`RestoreStage`] is a second enum. A single sink would let a creation emit
/// `restoring_databases`, which is a claim about a live account.
///
/// It never blocks, for the reason its sibling gives: a dropped progress report
/// costs nothing, and a restore stalled between two `DROP DATABASE` statements
/// because a client stopped reading costs a great deal. The terminal message is
/// the handler's.
pub struct ChannelRestoreSink {
    /// The sending half of the stream's channel.
    responses: Sender<Result<RestoreBackupResponse, Status>>,
}

impl ChannelRestoreSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(responses: Sender<Result<RestoreBackupResponse, Status>>) -> Self {
        Self { responses }
    }
}

impl RestoreSink for ChannelRestoreSink {
    /// Sends one `Progress` message, or drops it when the client is not
    /// keeping up.
    fn report(&mut self, stage: RestoreStage, percent: u32) {
        let _ = self.responses.try_send(Ok(RestoreBackupResponse {
            result: Some(restore_backup_response::Result::Progress(Progress {
                percent,
                stage: stage.as_str().to_owned(),
            })),
        }));
    }
}
