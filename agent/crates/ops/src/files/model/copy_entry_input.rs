//! Everything one copy is told, already validated.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::transfer_bounds::TransferBounds;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to copy a file or a directory tree within an account's home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CopyEntryInput {
    /// The account whose uid the copy runs as, and whose home contains both paths.
    pub account: AccountName,
    /// The entry to copy.
    pub source: MutablePath,
    /// Where the copy goes. Must not exist; its missing parents are created.
    pub destination: MutablePath,
    /// The most entries and bytes the copy may hold, as the request stated them.
    pub bounds: TransferBounds,
}
