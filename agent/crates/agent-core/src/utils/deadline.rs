//! The instant a bounded wait gives up.

use std::time::{Duration, Instant};

/// A point in time after which a wait is over.
///
/// A duration too large to add to the clock (the type allows it) means "no
/// instant this process will live to see", not a panic: `Instant + Duration`
/// panics on overflow, and a root process must not.
#[derive(Debug, Clone, Copy)]
pub(crate) struct Deadline {
    /// The instant, or `None` when `after` overflowed the clock.
    at: Option<Instant>,
}

impl Deadline {
    /// The instant `span` from now.
    pub(crate) fn after(span: Duration) -> Self {
        Self {
            at: Instant::now().checked_add(span),
        }
    }

    /// Whether the instant has been reached.
    pub(crate) fn has_passed(&self) -> bool {
        self.at.is_some_and(|at| Instant::now() >= at)
    }
}

#[cfg(test)]
#[path = "../tests/utils/deadline_tests.rs"]
mod tests;
