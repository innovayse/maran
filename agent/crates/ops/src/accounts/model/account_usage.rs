//! What an account currently occupies on disk.

/// An account's disk usage and the quota it is measured against.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountUsage {
    /// Bytes currently used by the account's home directory tree.
    pub used_bytes: u64,
    /// The quota in force, in bytes; zero means no quota is set.
    pub quota_bytes: u64,
}
