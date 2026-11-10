//! The [`InventorySink`] that puts an inventory read's messages onto the gRPC stream.

use maran_ops::transfer::inventory::model::inventory_sink::InventorySink;
use maran_ops::transfer::inventory::model::inventory_step::InventoryStep;
use maran_ops::transfer::inventory::model::source_account_record::SourceAccountRecord;
use maran_ops::transfer::inventory::model::source_warning_record::SourceWarningRecord;
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{
    ReadSourceInventoryProgress, ReadSourceInventoryResponse, SourceFailureReason,
    read_source_inventory_response,
};
use crate::services::transfer::to_wire_inventory_step::to_wire_inventory_step;
use crate::services::transfer::to_wire_source_account::to_wire_source_account;
use crate::services::transfer::to_wire_source_warning::to_wire_source_warning;
use crate::services::wire::deadline_sender::DeadlineSender;

/// Delivers an inventory read's progress, accounts and warnings into the bounded
/// channel behind the response stream.
///
/// The bound (a full channel makes the send wait, a closed one fails it, one that
/// stays full past the patience fails it too) lives in [`DeadlineSender`], shared
/// with the export, the log tail and the file read. The read is cancellation class
/// "stop": a failed send is remembered, [`InventorySink::is_open`] answers false
/// from then on, and the read ends at its next check without asking the source
/// for anything more. The trait methods return nothing, so the failure is state
/// here and not a value there.
pub struct ChannelInventorySink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<ReadSourceInventoryResponse, Status>>,
    /// Whether a send has failed, which means the client is gone or stalled.
    stopped: bool,
}

impl ChannelInventorySink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(messages: Sender<Result<ReadSourceInventoryResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(messages),
            stopped: false,
        }
    }

    /// Delivers the terminal message under the same deadline every other message
    /// gets, with the connection reason beside it when there is one.
    ///
    /// Through THIS type and not a second clone of the sender, for the reason the
    /// export gives: the ending it is most often sent on is a stalled client,
    /// where a plain blocking send has no bound at all. The outcome is discarded;
    /// what the bound buys is that the thread comes back.
    pub fn terminal(
        &mut self,
        terminal: read_source_inventory_response::Result,
        reason: SourceFailureReason,
    ) {
        let _ = self.sender.send(Ok(ReadSourceInventoryResponse {
            result: Some(terminal),
            failure_reason: reason as i32,
        }));
    }

    /// Sends one non-terminal message and remembers a failed send.
    fn deliver(&mut self, message: read_source_inventory_response::Result) {
        if self.stopped {
            return;
        }
        let sent = self.sender.send(Ok(ReadSourceInventoryResponse {
            result: Some(message),
            failure_reason: SourceFailureReason::Unspecified as i32,
        }));
        if sent.is_err() {
            self.stopped = true;
        }
    }
}

impl InventorySink for ChannelInventorySink {
    /// Sends the step the read has reached.
    fn progress(&mut self, step: InventoryStep) {
        self.deliver(read_source_inventory_response::Result::Progress(
            ReadSourceInventoryProgress {
                step: to_wire_inventory_step(step) as i32,
            },
        ));
    }

    /// Sends one complete account.
    fn account(&mut self, account: SourceAccountRecord) {
        self.deliver(read_source_inventory_response::Result::Account(
            to_wire_source_account(&account),
        ));
    }

    /// Sends one warning.
    fn warning(&mut self, warning: SourceWarningRecord) {
        self.deliver(read_source_inventory_response::Result::Warning(
            to_wire_source_warning(&warning),
        ));
    }

    /// Whether the client is still there: no send has failed and the channel is
    /// open.
    fn is_open(&self) -> bool {
        !self.stopped && self.sender.is_open()
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/channel_inventory_sink_tests.rs"]
mod tests;
