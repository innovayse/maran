//! One thing a creation will put into the archive.

use super::member_kind::MemberKind;

/// A file or directory found by the measuring walk, by its path below the home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct PlannedEntry {
    /// The `/`-joined components below the account's home.
    pub(crate) path: String,
    /// File or directory; anything else would have refused the plan.
    pub(crate) kind: MemberKind,
}
