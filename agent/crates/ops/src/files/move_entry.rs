//! MoveEntry: one rename inside a customer's home.

use crate::files::model::move_entry_input::MoveEntryInput;
use crate::files::{FilesHost, FilesOpError};

/// Moves or renames one entry inside an account's home, as the account.
///
/// A single rename that never replaces anything and never leaves the
/// filesystem: a destination that exists, or the same path as the source, is
/// [`FilesOpError::AlreadyExists`] and both entries are left as they were; a
/// missing source is [`FilesOpError::NotFound`], which is also what a retry of a
/// move that already happened sees. A directory moved into its own subtree is
/// [`FilesOpError::IntoItself`], a destination on another filesystem is
/// [`FilesOpError::CrossesDevice`] (there is no copy-and-delete fallback, so a
/// move can never end half done), and a symlink at either path is
/// [`FilesOpError::UnsupportedEntry`]. The destination's parents are created.
///
/// # Errors
///
/// The verdicts above, plus [`FilesOpError::Privilege`],
/// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::NotOwned`], [`FilesOpError::QuotaExceeded`] and
/// [`FilesOpError::MutationFailed`].
pub fn move_entry(host: &dyn FilesHost, input: &MoveEntryInput) -> Result<(), FilesOpError> {
    host.move_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/move_entry_tests.rs"]
mod tests;
