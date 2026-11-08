//! Everything one tree removal is told, already validated.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to remove a file, a link or a whole directory tree.
///
/// Distinct from [`super::delete_entry_input::DeleteEntryInput`], which names
/// the narrow single-file removal the ACME cleanup uses and which deliberately
/// cannot be aimed at a tree.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ForceDeleteEntryInput {
    /// The account whose uid the removal runs as, and whose home holds the entry.
    pub account: AccountName,
    /// The entry to remove, relative to that home.
    pub path: MutablePath,
}
