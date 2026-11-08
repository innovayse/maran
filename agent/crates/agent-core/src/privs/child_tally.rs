//! The two figures a child hands to the fork when its work succeeded.

/// The success answer of a forked job, before it crosses to the parent.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ChildTally {
    /// The first figure.
    first: u64,
    /// The second figure.
    second: u64,
}

impl ChildTally {
    /// A tally of two figures.
    #[must_use]
    pub const fn new(first: u64, second: u64) -> Self {
        Self { first, second }
    }

    /// The first figure.
    #[must_use]
    pub const fn first(self) -> u64 {
        self.first
    }

    /// The second figure.
    #[must_use]
    pub const fn second(self) -> u64 {
        self.second
    }
}
