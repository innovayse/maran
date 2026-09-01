//! The [`LogSink`] that puts a tailed log line onto the gRPC stream.

use std::time::{Duration, Instant};

use maran_ops::sites::{LogSink, TailEnd};
use tokio::sync::mpsc::Sender;
use tokio::sync::mpsc::error::TrySendError;
use tonic::Status;

use crate::proto::{TailSiteLogLine, TailSiteLogResponse, tail_site_log_response};

/// How long a line may wait for a client that has stopped reading.
///
/// The bound the [`LogSink`] contract demands, and the reason it cannot be a
/// plain `blocking_send`. A client that stops reading WITHOUT closing — an
/// exhausted HTTP/2 flow-control window, a suspended laptop, a paused debugger
/// — fills the channel and then never drains it. `blocking_send` would park the
/// tail's blocking-pool thread there for the life of the process, between the
/// two places the follow loop checks its guards, so neither the liveness check
/// nor the idle deadline could ever fire. Tokio's blocking pool is 512 threads
/// and every site, SSL, PHP and account operation goes through it, so enough
/// wedged tabs would stop the whole agent.
///
/// Thirty seconds: far longer than any real reader needs for a 64-slot channel,
/// far shorter than "forever". A client that exceeds it has its stream ended
/// and can simply reopen it.
const SEND_TIMEOUT: Duration = Duration::from_secs(30);

/// How long to wait between two attempts while the channel is full.
///
/// A poll rather than an async wait because this runs on a blocking thread with
/// no runtime to await on, and `try_send` hands the message back on a full
/// channel so nothing is lost by retrying.
const RETRY_INTERVAL: Duration = Duration::from_millis(50);

/// Delivers a tail's lines into the bounded channel behind the response stream.
///
/// The whole of the backpressure and the whole of the cancellation. A full
/// channel makes the send wait, so a slow client slows the tail instead of
/// growing a queue inside the root daemon; a closed channel makes it fail,
/// which answers "stop"; and a channel that stays full past `SEND_TIMEOUT`
/// also answers "stop", because a client that is not reading is not a slow
/// client, it is an absent one.
///
/// [`LogSink::is_listening`] answers the same question when no line has arrived
/// to ask it with — the case that would otherwise leave a tail on a silent log
/// polling for the life of the process, since a `spawn_blocking` task cannot be
/// aborted from outside.
pub struct StreamLogSink {
    /// The sending half of the stream's channel.
    lines: Sender<Result<TailSiteLogResponse, Status>>,
    /// How long a line may wait before the client is treated as absent.
    patience: Duration,
}

impl StreamLogSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(lines: Sender<Result<TailSiteLogResponse, Status>>) -> Self {
        Self::with_patience(lines, SEND_TIMEOUT)
    }

    /// The same sink with an explicit deadline.
    ///
    /// Exists so the give-up path can be TESTED rather than reasoned about: a
    /// test that had to wait out `SEND_TIMEOUT` would take half a minute, so
    /// nobody would write it, and the branch that drops a wedged client would
    /// ship on an argument instead of on a red-when-removed test. Production
    /// has one caller and it is [`Self::new`].
    #[must_use]
    pub fn with_patience(
        lines: Sender<Result<TailSiteLogResponse, Status>>,
        patience: Duration,
    ) -> Self {
        Self { lines, patience }
    }
}

impl LogSink for StreamLogSink {
    /// Sends one line within `SEND_TIMEOUT`, and names the ending if it cannot.
    fn line(&mut self, line: &str, historical: bool) -> Result<(), TailEnd> {
        let mut message = Ok(TailSiteLogResponse {
            result: Some(tail_site_log_response::Result::Ok(TailSiteLogLine {
                line: line.to_owned(),
                historical,
            })),
        });

        let deadline = Instant::now() + self.patience;

        loop {
            match self.lines.try_send(message) {
                Ok(()) => return Ok(()),
                // The receiver is gone: tonic tore the stream down, and there
                // is nobody to send the rest to. Reported as the client's own
                // ending, so nothing is sent about it.
                Err(TrySendError::Closed(_)) => return Err(TailEnd::ClientClosed),
                Err(TrySendError::Full(returned)) => {
                    if Instant::now() >= deadline {
                        // The agent's decision, not the client's: the stream is
                        // still open and the operator is owed an explanation.
                        return Err(TailEnd::ClientStalled);
                    }
                    // `try_send` hands the message back, so a retry costs
                    // nothing and drops nothing.
                    message = returned;
                    std::thread::sleep(RETRY_INTERVAL);
                }
            }
        }
    }

    /// Whether the receiving half is still open — asked once per poll, with no
    /// line needed to ask it.
    fn is_listening(&mut self) -> bool {
        !self.lines.is_closed()
    }
}

#[cfg(test)]
#[path = "../../tests/services/sites/stream_log_sink_tests.rs"]
mod tests;
