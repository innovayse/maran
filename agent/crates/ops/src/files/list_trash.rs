//! ListTrash: what the account's trash holds.

use maran_agent_core::validation::system::name::AccountName;

use crate::files::model::trashed_entry::TrashedEntry;
use crate::files::{FilesHost, FilesOpError};

/// Lists the account's trash, newest first, at most 10 000 entries.
///
/// Read-only. Anything in the trash directory that is not in the shape the agent
/// writes is left out and not reported, and nothing in it is trusted: the
/// customer can edit their own trash. A trash never used is an empty list.
///
/// # Errors
///
/// Returns [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`] and [`FilesOpError::TooLarge`] when the
/// trash holds more names than a listing reads.
pub fn list_trash(
    host: &dyn FilesHost,
    account: &AccountName,
) -> Result<Vec<TrashedEntry>, FilesOpError> {
    host.list_trash_as_account(account)
}

#[cfg(test)]
#[path = "../tests/files/list_trash_tests.rs"]
mod tests;
