//! The clock [`StreamLogSink`](super::stream_log_sink::StreamLogSink) measures
//! its patience on.

use std::time::{Duration, Instant};

/// The passage of time as the log sink observes it.
///
/// The sink's whole contract is a deadline, so the deadline is the one thing its
/// tests must be able to observe. Reading the ambient clock and sleeping on the
/// real one leaves only two ways to check it: measure elapsed wall time, which
/// answers differently on a loaded machine, or wait the deadline out, which
/// nobody does. Both were present here, and one of them failed spuriously under
/// load.
///
/// With the clock injected, a test drives virtual time and can assert BOTH
/// halves of the bound exactly — that the sink waited the whole deadline, and
/// that it waited no longer than one retry past it — without a single sleep.
///
/// Production has exactly one implementation,
/// [`SystemSinkClock`](super::system_sink_clock::SystemSinkClock).
pub trait SinkClock: Send {
    /// The current instant.
    fn now(&mut self) -> Instant;

    /// Waits for `interval` before the caller's next attempt.
    ///
    /// Takes `&mut self` so a test clock can use the wait as its synchronisation
    /// point — advancing virtual time, or letting a reader drain one slot —
    /// instead of the caller racing a real reader against a real duration.
    fn wait(&mut self, interval: Duration);
}
