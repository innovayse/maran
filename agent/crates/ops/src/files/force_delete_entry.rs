//! ForceDeleteEntry: a file, a link or a whole tree out of a customer's home.

use crate::files::model::force_delete_entry_input::ForceDeleteEntryInput;
use crate::files::{FilesHost, FilesOpError};

/// Removes a file, a link or a directory tree inside an account's home, as the
/// account, and answers how many entries were removed.
///
/// The file manager's delete; [`super::delete_entry`] is not, and stays narrow for
/// the ACME cleanup that calls it. A symlink is unlinked and never followed, at
/// any depth. A hardlinked file is unlinked by name and the other names keep the
/// data. A directory is emptied depth first through descriptors and never across
/// onto another filesystem. A tree deeper than the ceiling is refused rather than
/// half deleted. Idempotent in the sense the agent can offer: a path that is gone
/// is [`FilesOpError::NotFound`], and a run that was stopped is finished by
/// running it again.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`], [`FilesOpError::NotOwned`],
/// [`FilesOpError::TooDeep`], [`FilesOpError::CrossesDevice`] and
/// [`FilesOpError::LimitReached`] as above; [`FilesOpError::Saturated`] at the
/// bulk bound; and [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`] and [`FilesOpError::RemoveFailed`].
pub fn force_delete_entry(
    host: &dyn FilesHost,
    input: &ForceDeleteEntryInput,
) -> Result<u64, FilesOpError> {
    host.force_delete_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/force_delete_entry_tests.rs"]
mod tests;
