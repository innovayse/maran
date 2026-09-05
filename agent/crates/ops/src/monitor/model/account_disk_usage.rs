//! What one hosting account occupies on disk.

use maran_agent_core::validation::system::name::AccountName;

/// One account's disk usage.
///
/// **Used bytes and nothing else.** There is deliberately no quota field here:
/// the panel sets every account's quota and stores it, so the agent measuring
/// one would be reading back a number the caller already has — and reading it
/// would mean parsing the quota tools' output on every dashboard refresh, on a
/// host where those tools may not be installed at all. The comparison of usage
/// against quota happens where the quota lives.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountDiskUsage {
    /// The account this measurement is about.
    pub account: AccountName,
    /// Bytes its home directory tree occupies.
    pub used_bytes: u64,
}
