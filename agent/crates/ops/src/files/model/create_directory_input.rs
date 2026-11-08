//! Everything one directory creation is told, already validated.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to create one directory inside an account's home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CreateDirectoryInput {
    /// The account whose uid the creation runs as, and whose home holds it.
    pub account: AccountName,
    /// The directory to create, relative to that home. Missing parents are
    /// created with the agent's directory mode; the final directory gets
    /// [`CreateDirectoryInput::mode`].
    pub path: MutablePath,
    /// Permission bits of the final directory, plain `rwxrwxrwx` by type.
    pub mode: FileMode,
}
