//! TrashEntry: an entry moved into the account's trash.

use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::model::trash_entry_input::TrashEntryInput;
use crate::files::{FilesHost, FilesOpError};

/// Moves one entry into the account's trash and answers its identifier there.
///
/// A rename inside the home's own filesystem, so it is instant whatever the size
/// and the entry keeps counting toward the account's quota. Refusals are those of
/// a move: a missing source is [`FilesOpError::NotFound`] and a symlink as the
/// path is [`FilesOpError::UnsupportedEntry`]; the home and the trash itself
/// cannot be named at all.
///
/// # Errors
///
/// As [`super::move_entry`], without the destination's: [`FilesOpError::Privilege`],
/// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::NotFound`], [`FilesOpError::UnsupportedEntry`],
/// [`FilesOpError::NotOwned`], [`FilesOpError::CrossesDevice`],
/// [`FilesOpError::QuotaExceeded`] and [`FilesOpError::MutationFailed`].
pub fn trash_entry(
    host: &dyn FilesHost,
    input: &TrashEntryInput,
) -> Result<TrashEntryId, FilesOpError> {
    host.trash_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/trash_entry_tests.rs"]
mod tests;
