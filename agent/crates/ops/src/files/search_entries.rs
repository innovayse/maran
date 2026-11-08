//! Finding entries by name inside a customer's home.

use crate::files::FilesOpError;
use crate::files::browse_host::BrowseHost;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;

/// Searches the account's home for entries whose name contains the query.
///
/// The search runs in a forked child as the account (the host's business), is
/// bounded in depth, results and entries read, and never follows a symlink or
/// crosses onto another filesystem. A bound that stops it makes the outcome
/// `truncated`; it is not an error. Idempotent: it only observes.
///
/// # Errors
///
/// Returns whatever the host refuses with: [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`] for the
/// starting point, [`FilesOpError::Saturated`] when too many browse
/// operations are in flight, [`FilesOpError::TooLarge`] when the encoded result
/// exceeded the channel, or a privilege failure.
pub fn search_entries(
    host: &dyn BrowseHost,
    input: &SearchInput,
) -> Result<SearchOutcome, FilesOpError> {
    host.search_as_account(input.account(), input)
}

#[cfg(test)]
#[path = "../tests/files/search_entries_tests.rs"]
mod tests;
