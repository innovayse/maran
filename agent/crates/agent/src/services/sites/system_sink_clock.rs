//! The real clock behind [`SinkClock`].

use std::time::{Duration, Instant};

use super::sink_clock::SinkClock;

/// The host's own clock: the only [`SinkClock`] production ever uses.
///
/// It sleeps the calling thread rather than awaiting, because the sink runs on a
/// blocking thread with no runtime to await on — that is the same reason the
/// sink polls a full channel instead of waiting on it.
#[derive(Debug, Default, Clone, Copy)]
pub struct SystemSinkClock;

impl SinkClock for SystemSinkClock {
    /// The host's current instant.
    fn now(&mut self) -> Instant {
        Instant::now()
    }

    /// Parks the calling thread for `interval`.
    fn wait(&mut self, interval: Duration) {
        std::thread::sleep(interval);
    }
}
