//! ListDirectory: one page of a directory in a customer's home.

use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::list_directory_input::ListDirectoryInput;
use crate::files::{BrowseHost, FilesOpError};

/// Lists one directory of an account's home, sorted and paged.
///
/// The read, the hidden filter and the symlink-safe `lstat` of every entry
/// happen in a child dropped to the account; the sort and the page are cut in
/// the agent, so the panel never needs the whole directory. A symlink is listed
/// as a link and its target text is not carried. Idempotent: it observes and
/// changes nothing.
///
/// # Errors
///
/// Returns [`FilesOpError::Saturated`] when too many browse operations are
/// running, [`FilesOpError::Privilege`] when the account cannot be resolved or
/// the drop fails, [`FilesOpError::HomeUnusable`] and
/// [`FilesOpError::DirectoryUnusable`] when the walk is refused or the path is
/// not a directory the account owns, and [`FilesOpError::TooLarge`] when the
/// directory holds more than 100 000 entries.
pub fn list_directory(
    host: &dyn BrowseHost,
    input: &ListDirectoryInput,
) -> Result<DirectoryListing, FilesOpError> {
    host.list_as_account(&input.account, &input.path, &input.window)
}

#[cfg(test)]
#[path = "../tests/files/list_directory_tests.rs"]
mod tests;
