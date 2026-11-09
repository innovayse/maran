//! Everything one retention purge is told, already validated.

use std::num::NonZeroU32;

use maran_agent_core::validation::system::name::AccountName;

/// A validated request to remove the trash entries older than a retention.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PurgeExpiredTrashInput {
    /// The account whose trash is purged.
    pub account: AccountName,
    /// Entries trashed more than this many days ago are removed. Non-zero by
    /// type: "older than zero days" can only mean "everything", which is
    /// `EmptyTrash` and must be asked for by name.
    pub older_than_days: NonZeroU32,
}
