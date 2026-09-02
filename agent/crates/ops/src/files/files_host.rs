//! The seam between the file operations and the machine they run on.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;

/// Everything the file operations do to this machine.
///
/// A trait for the same reason `SiteHost` is one: every method here forks,
/// drops to a hosting account and touches a real customer's home, which is
/// precisely what a unit test must never actually do. The one implementation
/// that touches the machine is [`super::ProcessFilesHost`], and it is the
/// smallest piece of the area — the decisions worth reviewing are in the
/// operations, and the hardening worth reviewing is in `write_in_home`,
/// `remove_in_home` and `open_parent_directory`, all three of which a test
/// drives directly against a temporary directory it owns.
///
/// Creating the parent directories is a separate call from writing the file so
/// that the two walks can be asked for different things — the first creates
/// what is missing, the second requires it — because a write that could also
/// build directories is a write that can be aimed at a tree nobody asked for.
/// It used to be separate so that a containment check could sit between them;
/// that check was deleted in review once it was established that it could not
/// fail (see `write_file`), and the remaining reason is the one above.
pub trait FilesHost: Send + Sync {
    /// Creates every missing directory on the way to `relative`, as `account`.
    ///
    /// The file's own name is not created — only the levels above it.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// underlying `fork_as_account` forks and blocks in `waitpid`, which on a
    /// runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::Privilege`] when the account cannot be resolved
    /// or the drop fails, [`FilesOpError::HomeUnusable`] when the home is not a
    /// directory the account owns, and [`FilesOpError::DirectoryUnusable`] when
    /// a level cannot be created or is not a directory the account owns.
    fn create_parents_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError>;

    /// Writes `contents` at `relative` with permission bits `mode`, as
    /// `account`, atomically.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`, as
    /// above.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::Privilege`],
    /// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`] and
    /// [`FilesOpError::WriteFailed`] as `write_in_home` documents them.
    fn write_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
        contents: &[u8],
        mode: FileMode,
    ) -> Result<(), FilesOpError>;

    /// Removes the file at `relative`, as `account`.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`, as
    /// above.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::Privilege`], [`FilesOpError::NotFound`],
    /// [`FilesOpError::NotARegularFile`] and [`FilesOpError::RemoveFailed`] as
    /// `remove_in_home` documents them.
    fn remove_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError>;

    /// Resolves `relative` inside `account`'s home, returning the canonical
    /// path, and reporting whether there is anything there at all.
    ///
    /// **Called by [`super::delete_entry`] and by nothing else**, and the reason
    /// is not containment — the descriptor walk is that, and it needs no help.
    /// This is the only thing in the area that can tell "there is no such entry"
    /// from "the child refused the entry", because the child's outcome crosses
    /// back as an exit status and carries no reason. A removal has to make that
    /// distinction to be idempotent; a write never has to locate anything, so it
    /// does not call this, and a version that did was deleted rather than kept
    /// as a check that could not fail.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::NotFound`] when the path does not exist and
    /// [`FilesOpError::EscapesHome`] when it resolves outside the home.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, FilesOpError>;
}
