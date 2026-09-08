//! The [`ProgressSink`] that puts a creation's progress onto the gRPC stream.

use maran_ops::backup::{BackupStage, ProgressSink};
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{CreateBackupResponse, Progress, create_backup_response};

/// Delivers a creation's progress into the bounded channel behind the response
/// stream.
///
/// [`ProgressSink::report`] is infallible and is called from the operation's
/// own thread between two pieces of real work, so this sink **never blocks**:
/// a full channel or a closed one drops the report. That is the right trade
/// here and it is not the one `StreamLogSink` makes, because the two carry
/// different things. A tailed log line is the payload — dropping it loses the
/// only copy — while a progress report is a number about work that is
/// happening anyway, and a backup half way through dumping a database is not
/// worth stalling because a browser tab stopped reading. The terminal message
/// is sent by the handler and not by this sink, so nothing that decides the
/// outcome can be dropped here.
pub struct ChannelProgressSink {
    /// The sending half of the stream's channel.
    responses: Sender<Result<CreateBackupResponse, Status>>,
}

impl ChannelProgressSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(responses: Sender<Result<CreateBackupResponse, Status>>) -> Self {
        Self { responses }
    }
}

impl ProgressSink for ChannelProgressSink {
    /// Sends one `Progress` message, or drops it when the client is not
    /// keeping up.
    ///
    /// The stage's wire name comes from [`BackupStage::as_str`] rather than
    /// from a literal here, so the names the panel switches on have one
    /// spelling and it lives with the operation that emits them.
    fn report(&mut self, stage: BackupStage, percent: u32) {
        let _ = self.responses.try_send(Ok(CreateBackupResponse {
            result: Some(create_backup_response::Result::Progress(Progress {
                percent,
                stage: stage.as_str().to_owned(),
            })),
        }));
    }
}
