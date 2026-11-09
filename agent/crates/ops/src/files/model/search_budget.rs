//! How many directory entries one search may read.

/// Entries one search reads before it stops: 200 000.
///
/// A count and not a clock, so the same tree always gives the same answer and a
/// test can reproduce the stop. It counts every name READ, matched or not,
/// hidden or not, representable or not: a customer controls how many names a
/// tree holds, so only a count of reads bounds the work.
const STANDARD_ENTRIES: usize = 200_000;

/// The entries a search may still read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SearchBudget {
    /// Names that may still be read.
    remaining: usize,
}

impl SearchBudget {
    /// The budget every search starts with.
    #[must_use]
    pub fn standard() -> Self {
        Self::of(STANDARD_ENTRIES)
    }

    /// A budget of `entries`; for a test that cannot build 200 000 names.
    #[must_use]
    pub(crate) fn of(entries: usize) -> Self {
        Self { remaining: entries }
    }

    /// Names that may still be read.
    #[must_use]
    pub fn remaining(&self) -> usize {
        self.remaining
    }

    /// Records that `entries` names were read, never going below zero.
    pub fn spend(&mut self, entries: usize) {
        self.remaining = self.remaining.saturating_sub(entries);
    }
}

#[cfg(test)]
#[path = "../../tests/files/model/search_budget_tests.rs"]
mod tests;
