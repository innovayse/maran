//! The [`ExportSink`] that puts an export's messages onto the gRPC stream.

use maran_ops::backup::package::signed_manifest::SignedManifest;
use maran_ops::backup::{ExportSink, ExportStopped};
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{
    ExportBackupChunk, ExportBackupHeader, ExportBackupResponse, export_backup_response,
};
use crate::services::wire::deadline_sender::DeadlineSender;

/// Delivers an export's header and chunks into the bounded channel behind the
/// response stream.
///
/// The bound (a full channel makes the send wait, a closed one fails it, one that
/// stays full past the patience fails it too) lives in [`DeadlineSender`], shared
/// with the log tail and the file read. A failed send is [`ExportStopped`]: the
/// export is cancellation class "stop", so it ends the read and releases the
/// account's lock.
pub struct ChannelExportSink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<ExportBackupResponse, Status>>,
}

impl ChannelExportSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(messages: Sender<Result<ExportBackupResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(messages),
        }
    }

    /// Delivers the terminal message under the same deadline a chunk gets.
    ///
    /// Through THIS type and not a second clone of the sender, for the reason the
    /// file read gives: the ending it is most often sent on is a stalled client,
    /// where a plain blocking send has no bound at all. The outcome is discarded;
    /// what the bound buys is that the thread comes back.
    pub fn terminal(&mut self, terminal: export_backup_response::Result) {
        let _ = self.sender.send(Ok(ExportBackupResponse {
            result: Some(terminal),
        }));
    }
}

impl ExportSink for ChannelExportSink {
    /// Sends the signed manifest as the stream's first message.
    fn header(&mut self, signed: &SignedManifest) -> Result<(), ExportStopped> {
        self.sender
            .send(Ok(ExportBackupResponse {
                result: Some(export_backup_response::Result::Header(ExportBackupHeader {
                    manifest: signed.manifest.clone(),
                    signature: signed.signature.clone(),
                    public_key: signed.public_key.clone(),
                })),
            }))
            .map_err(|_end| ExportStopped)
    }

    /// Sends one slice of the artifact within the patience.
    fn chunk(&mut self, data: &[u8]) -> Result<(), ExportStopped> {
        self.sender
            .send(Ok(ExportBackupResponse {
                result: Some(export_backup_response::Result::Chunk(ExportBackupChunk {
                    data: data.to_vec(),
                })),
            }))
            .map_err(|_end| ExportStopped)
    }
}
