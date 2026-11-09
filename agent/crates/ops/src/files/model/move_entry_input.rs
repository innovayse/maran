//! Everything one move or rename is told, already validated.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to move one entry within an account's home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MoveEntryInput {
    /// The account whose uid the move runs as, and whose home contains both paths.
    pub account: AccountName,
    /// The entry to move.
    pub source: MutablePath,
    /// The name it gets. Must not exist; its missing parents are created.
    pub destination: MutablePath,
}
