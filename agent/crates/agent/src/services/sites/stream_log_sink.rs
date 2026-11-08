//! The [`LogSink`] that puts a tailed log line onto the gRPC stream.

use std::time::Duration;

use maran_ops::sites::{LogSink, TailEnd};
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{AgentError, TailSiteLogLine, TailSiteLogResponse, tail_site_log_response};
use crate::services::wire::deadline_sender::DeadlineSender;
use crate::services::wire::send_end::SendEnd;
use crate::services::wire::sink_clock::SinkClock;

/// Delivers a tail's lines into the bounded channel behind the response stream.
///
/// The bound itself (a full channel makes the send wait, a closed one makes it
/// fail, one that stays full past the patience also fails) lives in
/// [`DeadlineSender`], shared with the file read; this type is the log tail's
/// half: it speaks the [`LogSink`] contract and names an ending as a
/// [`TailEnd`].
///
/// [`LogSink::is_listening`] answers the same question when no line has arrived
/// to ask it with — the case that would otherwise leave a tail on a silent log
/// polling for the life of the process, since a `spawn_blocking` task cannot be
/// aborted from outside.
pub struct StreamLogSink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<TailSiteLogResponse, Status>>,
}

impl StreamLogSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(lines: Sender<Result<TailSiteLogResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(lines),
        }
    }

    /// The same sink with an explicit deadline AND an explicit clock, so the
    /// give-up path is observable without waiting it out (see
    /// [`DeadlineSender::with_patience_on_clock`]). Production has one caller
    /// and it is [`Self::new`].
    #[must_use]
    pub fn with_patience_on_clock(
        lines: Sender<Result<TailSiteLogResponse, Status>>,
        patience: Duration,
        clock: Box<dyn SinkClock>,
    ) -> Self {
        Self {
            sender: DeadlineSender::with_patience_on_clock(lines, patience, clock),
        }
    }

    /// Delivers the tail's terminal message under the same deadline a line gets.
    ///
    /// The terminal message travels through THIS type rather than through a
    /// second clone of the sender, and the reason is the ending it is most
    /// often sent on. [`TailEnd::ClientStalled`] is reached only when the
    /// channel has been full for the whole patience with the receiver still
    /// open — so on exactly that path a plain `blocking_send` of the terminal
    /// message is a send into a full channel nobody is draining, and it parks
    /// the tail's blocking-pool thread with no bound at all.
    ///
    /// The outcome is deliberately discarded: a client that is gone, and a
    /// client that is still not reading a whole deadline later, are both
    /// clients there is nothing further to do about. What the bound buys is
    /// that the thread comes back either way.
    pub fn terminal(&mut self, error: AgentError) {
        let _ = self.sender.send(Ok(TailSiteLogResponse {
            result: Some(tail_site_log_response::Result::Error(error)),
        }));
    }
}

impl LogSink for StreamLogSink {
    /// Sends one line within the sender's patience, and names the ending if it cannot.
    fn line(&mut self, line: &str, historical: bool) -> Result<(), TailEnd> {
        self.sender
            .send(Ok(TailSiteLogResponse {
                result: Some(tail_site_log_response::Result::Ok(TailSiteLogLine {
                    line: line.to_owned(),
                    historical,
                })),
            }))
            .map_err(|end| match end {
                SendEnd::Closed => TailEnd::ClientClosed,
                SendEnd::Stalled => TailEnd::ClientStalled,
            })
    }

    /// Whether the receiving half is still open — asked once per poll, with no
    /// line needed to ask it.
    fn is_listening(&mut self) -> bool {
        self.sender.is_open()
    }
}

#[cfg(test)]
#[path = "../../tests/services/sites/stream_log_sink_tests.rs"]
mod tests;
