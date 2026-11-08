//! Everything one move into the trash is told, already validated.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to move one entry into the account's trash.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrashEntryInput {
    /// The account whose uid the move runs as, and whose home holds the entry
    /// and its trash.
    pub account: AccountName,
    /// The entry to trash, relative to that home.
    pub path: MutablePath,
}
