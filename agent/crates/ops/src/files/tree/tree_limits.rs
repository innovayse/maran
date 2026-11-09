//! The bounds a walk of a customer's tree keeps.

/// How much of a tree one operation may visit, and the ceilings the agent keeps
/// whatever a request says.
///
/// Counted while walking, not read from a header or a stat: a bound that trusts
/// an earlier estimate is a bound a racing customer grows. `max_entries` counts
/// files and directories together, the root of the operation included.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct TreeLimits {
    /// The most entries the walk may count.
    pub(crate) max_entries: u64,
    /// The most bytes of file content the walk may count.
    pub(crate) max_bytes: u64,
}

impl TreeLimits {
    /// Deepest tree the agent walks: a directory 128 levels below the root of
    /// the operation is the last one entered, and a deeper tree is refused as a
    /// whole. Also the bound on the recursion, so a customer's own nesting can
    /// never become the agent's stack depth.
    pub(crate) const MAXIMUM_DEPTH: usize = 128;

    /// Most entries one operation counts whatever the request stated (ten times
    /// the panel's default for a copy). A removal that reaches it stops and is
    /// finished by running it again.
    pub(crate) const CEILING_ENTRIES: u64 = 1_000_000;

    /// Most bytes of content one copy counts whatever the request stated (ten
    /// times the panel's default).
    pub(crate) const CEILING_BYTES: u64 = 20 * 1024 * 1024 * 1024;

    /// The bounds a request stated, clamped to the agent's ceilings.
    #[must_use]
    pub(crate) fn stated(max_entries: u64, max_bytes: u64) -> Self {
        Self {
            max_entries: max_entries.min(Self::CEILING_ENTRIES),
            max_bytes: max_bytes.min(Self::CEILING_BYTES),
        }
    }

    /// The ceilings alone, for an operation whose request states no allowance
    /// (a permission change or a removal).
    #[must_use]
    pub(crate) fn ceilings() -> Self {
        Self {
            max_entries: Self::CEILING_ENTRIES,
            max_bytes: u64::MAX,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/tree/tree_limits_tests.rs"]
mod tests;
