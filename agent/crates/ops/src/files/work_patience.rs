//! How long the parent waits for a file-manager child.

use std::time::Duration;

/// How long the parent gives a forked file-manager child before it kills it.
///
/// The child is killed at the end of its patience and the operation is
/// re-runnable (every mutation here is idempotent or staged), so a hung or
/// enormous job costs one blocking-pool thread for at most this long.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum WorkPatience {
    /// One entry: a rename, a mode, a directory. The same two minutes
    /// `fork_as_account` allows.
    Single,
    /// A tree: a copy, a removal, a recursive change, a purge. Ten minutes.
    Bulk,
}

impl WorkPatience {
    /// The wait this patience stands for.
    #[must_use]
    pub(crate) fn duration(self) -> Duration {
        match self {
            Self::Single => Duration::from_secs(120),
            Self::Bulk => Duration::from_secs(600),
        }
    }
}

#[cfg(test)]
#[path = "../tests/files/work_patience_tests.rs"]
mod tests;
