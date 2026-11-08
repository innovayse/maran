//! The [`BrowseHost`] that actually touches this machine.

use std::io::Write;
use std::os::unix::fs::MetadataExt;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account_for_bytes::fork_as_account_for_bytes;
use maran_agent_core::privs::fork_as_account_for_descriptor::fork_as_account_for_descriptor;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::bounded_slots::BoundedSlots;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::account_home::account_home;
use crate::files::decode_search_outcome::decode_search_outcome;
use crate::files::encode_search_outcome::encode_search_outcome;
use crate::files::list_in_home::list_in_home;
use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::entry_record_codec::{decode_listing, encode_listing};
use crate::files::model::listing_answer::ListingAnswer;
use crate::files::model::listing_window::ListingWindow;
use crate::files::model::opened_for_read::OpenedForRead;
use crate::files::model::page_listing::page_listing;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;
use crate::files::read_in_home::read_in_home;
use crate::files::search_in_home::search_in_home;
use crate::files::{BrowseHost, FilesOpError};

/// Most bytes a listing's answer may take through the byte channel (16 MiB).
const LISTING_CEILING: usize = 16 * 1024 * 1024;

/// Most bytes a search's answer may take through the byte channel (4 MiB).
const SEARCH_CEILING: usize = 4 * 1024 * 1024;

/// The most browse operations in flight across every account.
///
/// Each forks a child and holds a blocking-pool thread for the length of a
/// directory read. Sixteen leaves one customer's UI loop no way to take a
/// meaningful share of the pool and the host's memory (a child's answer is up to
/// 16 MiB).
const HOST_BROWSE_SLOTS: usize = 16;

/// The most browse operations one account may have in flight.
///
/// Four, so a file manager that lists, searches and reads at once is not
/// refused; the host-wide count bounds the sum of accounts.
const ACCOUNT_BROWSE_SLOTS: usize = 4;

/// Forks to the account for every browse operation, within a bound.
///
/// The only implementation that touches the machine, and the smallest piece of
/// the area: it resolves the account's ids, takes a slot, forks, and treats what
/// comes back as hostile — the bytes are decoded by a strict bounded codec and
/// the received descriptor is re-checked before a byte is read from it. What a
/// reviewer of the privileged read should read is in `list_in_home`,
/// `read_in_home` and `search_in_home`.
#[derive(Debug)]
pub struct ProcessBrowseHost {
    /// How many browse operations may run at once.
    slots: BoundedSlots,
}

impl ProcessBrowseHost {
    /// Creates the host with the production bounds: 4 per account, 16 per host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            slots: BoundedSlots::new(HOST_BROWSE_SLOTS, ACCOUNT_BROWSE_SLOTS),
        }
    }

    /// Maps a failed fork onto this area's vocabulary.
    ///
    /// The child's reason cannot cross an exit status, so `WorkFailed` is the
    /// walk or the read refusing, reported as `refused`.
    fn map_fork_error(error: PrivError, refused: FilesOpError) -> FilesOpError {
        match error {
            PrivError::WorkFailed => refused,
            PrivError::ResultTooLarge => FilesOpError::TooLarge,
            other => FilesOpError::Privilege(other),
        }
    }
}

impl Default for ProcessBrowseHost {
    fn default() -> Self {
        Self::new()
    }
}

impl BrowseHost for ProcessBrowseHost {
    /// Reads the directory in a forked child and pages the answer here.
    fn list_as_account(
        &self,
        account: &AccountName,
        path: &BrowsePath,
        window: &ListingWindow,
    ) -> Result<DirectoryListing, FilesOpError> {
        let _slot = self.slots.try_acquire(account)?;
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();
        let include_hidden = window.include_hidden();

        let bytes = fork_as_account_for_bytes(&ids, LISTING_CEILING, |sink| {
            let answer = list_in_home(&home, path, include_hidden, uid)
                .map_err(|_| PrivError::WorkFailed)?;
            sink.write_all(&encode_listing(&answer))
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| Self::map_fork_error(error, FilesOpError::DirectoryUnusable))?;

        // A malformed answer is the child failing to produce one, never a
        // partial listing.
        match decode_listing(&bytes).map_err(|_| FilesOpError::Privilege(PrivError::WorkFailed))? {
            ListingAnswer::Entries(listed) => Ok(page_listing(listed, window)),
            ListingAnswer::TooLarge => Err(FilesOpError::TooLarge),
        }
    }

    /// Opens the file in a forked child, receives the descriptor and re-checks it.
    ///
    /// The child's claim that the file is fine is not evidence: the descriptor is
    /// `fstat`ed here and must be a regular file, owned by the account, with
    /// exactly one link, before anything reads from it.
    fn open_for_read_as_account(
        &self,
        account: &AccountName,
        path: &BrowsePath,
    ) -> Result<OpenedForRead, FilesOpError> {
        let slot = self.slots.try_acquire(account)?;
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        let file = fork_as_account_for_descriptor(&ids, || {
            read_in_home(&home, path, uid).map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| Self::map_fork_error(error, FilesOpError::NotARegularFile))?;

        let metadata = file.metadata().map_err(|_| FilesOpError::NotARegularFile)?;
        if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
            return Err(FilesOpError::NotARegularFile);
        }
        Ok(OpenedForRead::new(file, slot))
    }

    /// Searches in a forked child and decodes the bounded answer here.
    fn search_as_account(
        &self,
        account: &AccountName,
        input: &SearchInput,
    ) -> Result<SearchOutcome, FilesOpError> {
        let _slot = self.slots.try_acquire(account)?;
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        let bytes = fork_as_account_for_bytes(&ids, SEARCH_CEILING, |sink| {
            let outcome = search_in_home(&home, input, uid).map_err(|_| PrivError::WorkFailed)?;
            sink.write_all(&encode_search_outcome(&outcome))
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| Self::map_fork_error(error, FilesOpError::DirectoryUnusable))?;

        decode_search_outcome(&bytes).map_err(|_| FilesOpError::Privilege(PrivError::WorkFailed))
    }
}
