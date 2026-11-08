//! RestoreTrashEntry: an entry moved back out of the account's trash.

use crate::files::model::restore_trash_entry_input::RestoreTrashEntryInput;
use crate::files::{FilesHost, FilesOpError};

/// Moves one trash entry back into the home and answers the path it now has.
///
/// Without a destination the entry goes back where it came from, a path read
/// from a file the customer can edit and validated like any other before it is
/// used. Nothing is replaced: a destination that exists is
/// [`FilesOpError::AlreadyExists`] and the entry stays in the trash; an unknown
/// identifier is [`FilesOpError::NotFound`]; an entry not in the shape the agent
/// writes, or whose recorded path does not validate, is
/// [`FilesOpError::InvalidTrashEntry`]. The destination's missing parents are
/// created.
///
/// # Errors
///
/// The verdicts above, plus [`FilesOpError::Privilege`],
/// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::CrossesDevice`], [`FilesOpError::QuotaExceeded`] and
/// [`FilesOpError::MutationFailed`].
pub fn restore_trash_entry(
    host: &dyn FilesHost,
    input: &RestoreTrashEntryInput,
) -> Result<String, FilesOpError> {
    host.restore_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/restore_trash_entry_tests.rs"]
mod tests;
