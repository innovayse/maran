//! What an account currently occupies on disk.

use super::quota_state::QuotaState;

/// An account's disk usage and the quota state it is measured against.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountUsage {
    /// Bytes currently used by the account's home directory tree.
    pub used_bytes: u64,
    /// Whether, and what, this account's home filesystem can enforce —
    /// never a bare byte count, because a byte count alone cannot distinguish
    /// "no limit configured" from "this filesystem cannot hold a limit at
    /// all." See [`QuotaState`].
    pub quota: QuotaState,
}
