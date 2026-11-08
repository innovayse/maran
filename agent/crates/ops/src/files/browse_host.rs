//! The seam between the browse operations and the machine they run on.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;
use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::listing_window::ListingWindow;
use crate::files::model::opened_for_read::OpenedForRead;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;

/// Everything the browse operations do to this machine.
///
/// Beside [`super::FilesHost`] and not inside it: the write side and the read
/// side evolve independently, and every method here forks, drops to a hosting
/// account and reads a real customer's home, which a unit test must never do.
/// The only implementation that touches the machine is
/// [`super::ProcessBrowseHost`].
///
/// Every method MUST be called from `tokio::task::spawn_blocking`: the fork
/// blocks in `waitpid`, which on a runtime worker stalls every other command.
pub trait BrowseHost: Send + Sync {
    /// Lists `path` as `account`, sorted and paged by `window`.
    ///
    /// # Errors
    ///
    /// As [`super::list_directory`].
    fn list_as_account(
        &self,
        account: &AccountName,
        path: &BrowsePath,
        window: &ListingWindow,
    ) -> Result<DirectoryListing, FilesOpError>;

    /// Opens the file `path` names for reading and hands back the descriptor.
    ///
    /// The browse slot taken for the open travels with the descriptor
    /// ([`OpenedForRead`]) and is released when the read ends, so a long
    /// download stays inside the per-account and per-host bounds.
    ///
    /// The descriptor has already been re-checked by the receiving side: a
    /// regular file, owned by the account, with exactly one link.
    ///
    /// # Errors
    ///
    /// [`FilesOpError::Saturated`], [`FilesOpError::Privilege`] and
    /// [`FilesOpError::NotARegularFile`] for every refusal of the child or of
    /// the re-check.
    fn open_for_read_as_account(
        &self,
        account: &AccountName,
        path: &BrowsePath,
    ) -> Result<OpenedForRead, FilesOpError>;

    /// Searches below a directory for entry names containing the query.
    ///
    /// # Errors
    ///
    /// [`FilesOpError::Saturated`], [`FilesOpError::Privilege`],
    /// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`] and
    /// [`FilesOpError::TooLarge`] when the answer outgrows its ceiling.
    fn search_as_account(
        &self,
        account: &AccountName,
        input: &SearchInput,
    ) -> Result<SearchOutcome, FilesOpError>;
}
