//! The growing gap between two looks at a running child.

use std::time::Duration;

/// First gap between two looks at a child.
///
/// Short, because the overwhelming majority of children finish in well under a
/// millisecond and the first look after the start usually finds them done.
const FIRST_NAP: Duration = Duration::from_millis(1);

/// Longest gap the interval grows to.
///
/// The gap doubles from [`FIRST_NAP`] so that a quick child is noticed almost
/// at once while a long one is not looked at tens of thousands of times. It is
/// also the latency of noticing that a child has exited when nothing else
/// wakes the loop.
const LONGEST_NAP: Duration = Duration::from_millis(20);

/// The gap to wait before the next look at a child, doubling up to a ceiling.
#[derive(Debug)]
pub(crate) struct PollInterval {
    /// The gap the next call to [`PollInterval::next_nap`] returns.
    current: Duration,
}

impl PollInterval {
    /// A fresh interval, starting at the shortest gap.
    pub(crate) const fn new() -> Self {
        Self { current: FIRST_NAP }
    }

    /// Returns the gap to wait now and doubles the one after it, up to the
    /// ceiling.
    pub(crate) fn next_nap(&mut self) -> Duration {
        let nap = self.current;
        self.current = (self.current * 2).min(LONGEST_NAP);
        nap
    }
}

#[cfg(test)]
#[path = "../tests/utils/poll_interval_tests.rs"]
mod tests;
