//! The bounded send into a response stream's channel, from a blocking thread.

use std::time::Duration;

use tokio::sync::mpsc::Sender;
use tokio::sync::mpsc::error::TrySendError;

use super::send_end::SendEnd;
use super::sink_clock::SinkClock;
use super::system_sink_clock::SystemSinkClock;

/// How long a message may wait for a client that has stopped reading.
///
/// The bound that makes this type exist, and the reason it cannot be a plain
/// `blocking_send`. A client that stops reading WITHOUT closing — an exhausted
/// HTTP/2 flow-control window, a suspended laptop, a paused debugger — fills the
/// channel and then never drains it. `blocking_send` would park a blocking-pool
/// thread there for the life of the process, between the places its loop checks
/// its guards, so no liveness check or idle deadline could ever fire. Tokio's
/// blocking pool is 512 threads and every site, SSL, PHP, account and file
/// operation goes through it, so enough wedged tabs would stop the whole agent.
///
/// Thirty seconds: far longer than any real reader needs for a small channel,
/// far shorter than "forever". A client that exceeds it has its stream ended and
/// can simply reopen it.
const SEND_TIMEOUT: Duration = Duration::from_secs(30);

/// How long to wait between two attempts while the channel is full.
///
/// A poll rather than an async wait because this runs on a blocking thread with
/// no runtime to await on, and `try_send` hands the message back on a full
/// channel so nothing is lost by retrying.
///
/// It is also the granularity of the deadline, and therefore the only slack the
/// tests allow above it: the sender gives up at the first attempt that finds the
/// deadline passed, so it can overshoot by at most one interval.
pub(crate) const RETRY_INTERVAL: Duration = Duration::from_millis(50);

/// Delivers messages into the bounded channel behind a response stream.
///
/// The whole of the backpressure and the whole of the cancellation for the two
/// streams that run on a blocking thread (`TailSiteLog` and `ReadFile`). A full
/// channel makes the send wait, so a slow client slows the producer instead of
/// growing a queue inside the root daemon; a closed channel makes it fail, which
/// answers "stop"; and a channel that stays full past the patience also answers
/// "stop", because a client that is not reading is not a slow client, it is an
/// absent one.
///
/// This was a private body of the log sink and became shared when the file read
/// needed the same bound: the second copy of a block moves to a named home
/// (rules/rust.md "One unit per file"). The two are copies in the sense the rule
/// uses — one could replace the other with nothing observable changing — and
/// only the message type and the vocabulary of the ending differ, which is why
/// the type is generic and the ending is the neutral [`SendEnd`].
pub struct DeadlineSender<T> {
    /// The sending half of the stream's channel.
    channel: Sender<T>,
    /// How long a message may wait before the client is treated as absent.
    patience: Duration,
    /// The clock the deadline and the retry interval are measured on.
    clock: Box<dyn SinkClock>,
}

impl<T> DeadlineSender<T> {
    /// Creates the sender around the stream's channel, on the host's own clock
    /// and with the agent's patience.
    #[must_use]
    pub fn new(channel: Sender<T>) -> Self {
        Self::with_patience_on_clock(channel, SEND_TIMEOUT, Box::new(SystemSinkClock))
    }

    /// The same sender with an explicit deadline AND an explicit clock.
    ///
    /// Exists so the give-up path can be TESTED rather than reasoned about: a
    /// test that had to wait out `SEND_TIMEOUT` would take half a minute, so
    /// nobody would write it, and a test measuring a shortened real deadline is
    /// a race with a shorter fuse. With the clock injected the deadline becomes
    /// something a test observes exactly, and the retry can carry a
    /// synchronisation point instead of a duration nobody can promise.
    /// Production has one caller and it is [`Self::new`].
    #[must_use]
    pub fn with_patience_on_clock(
        channel: Sender<T>,
        patience: Duration,
        clock: Box<dyn SinkClock>,
    ) -> Self {
        Self {
            channel,
            patience,
            clock,
        }
    }

    /// Sends one message, retrying a full channel until the deadline.
    ///
    /// # Errors
    ///
    /// Returns [`SendEnd::Closed`] when the receiving half is gone, and
    /// [`SendEnd::Stalled`] when the channel was still full at the deadline.
    pub fn send(&mut self, mut message: T) -> Result<(), SendEnd> {
        let deadline = self.clock.now() + self.patience;

        loop {
            match self.channel.try_send(message) {
                Ok(()) => return Ok(()),
                // The receiver is gone: tonic tore the stream down, and there
                // is nobody to send the rest to.
                Err(TrySendError::Closed(_)) => return Err(SendEnd::Closed),
                Err(TrySendError::Full(returned)) => {
                    if self.clock.now() >= deadline {
                        return Err(SendEnd::Stalled);
                    }
                    // `try_send` hands the message back, so a retry costs
                    // nothing and drops nothing.
                    message = returned;
                    self.clock.wait(RETRY_INTERVAL);
                }
            }
        }
    }

    /// Whether the receiving half is still open — asked without a message to
    /// ask it with, so a producer with nothing to send can still notice that
    /// its client left.
    #[must_use]
    pub fn is_open(&self) -> bool {
        !self.channel.is_closed()
    }
}

#[cfg(test)]
#[path = "../../tests/services/wire/deadline_sender_tests.rs"]
mod tests;
