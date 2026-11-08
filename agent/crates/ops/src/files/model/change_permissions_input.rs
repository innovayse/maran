//! Everything one permission change is told, already validated.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to change the permission bits of an entry, or of a tree.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ChangePermissionsInput {
    /// The account whose uid the change runs as, and whose home holds the entry.
    pub account: AccountName,
    /// The entry to change, relative to that home.
    pub path: MutablePath,
    /// The bits for a file, and for the entry itself when it is not walked.
    pub mode: FileMode,
    /// The bits for every directory of a recursive change. The service layer
    /// resolves the wire's "0 means the same as `mode`" before this exists, so
    /// no code below ever has to know that convention. Not read when
    /// [`ChangePermissionsInput::recursive`] is false.
    pub directory_mode: FileMode,
    /// Whether the tree below a directory is changed too.
    pub recursive: bool,
}
