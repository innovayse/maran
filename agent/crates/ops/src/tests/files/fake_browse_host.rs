//! The in-memory [`BrowseHost`] the browse operation tests drive.
//!
//! The real host forks, drops to a hosting account and reads a real customer's
//! home, none of which a unit test may do. What a unit test pins with this fake
//! is the SHAPE of an operation: what it hands the host. The hardening is tested
//! for real against a temporary directory in `open_directory_in_home_tests` and
//! `list_in_home_tests`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::listing_window::ListingWindow;
use crate::files::model::opened_for_read::OpenedForRead;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;
use crate::files::{BrowseHost, FilesOpError};

/// One listing the host was asked for.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct ListCall {
    /// The account named.
    pub(crate) account: String,
    /// The path named, as `/`-joined components.
    pub(crate) path: String,
    /// The window handed over.
    pub(crate) window: ListingWindow,
}

/// A host that records listings and answers what it was told to.
pub(crate) struct FakeBrowseHost {
    /// Every listing asked for, in order.
    list_calls: Mutex<Vec<ListCall>>,
    /// What `list_as_account` answers; a function because the error type is not `Clone`.
    listing: Box<dyn Fn() -> Result<DirectoryListing, FilesOpError> + Send + Sync>,
}

impl FakeBrowseHost {
    /// A host that answers every listing with `listing`.
    pub(crate) fn answering(listing: DirectoryListing) -> Self {
        Self {
            list_calls: Mutex::new(Vec::new()),
            listing: Box::new(move || Ok(listing.clone())),
        }
    }

    /// A host that refuses every listing with `error`.
    pub(crate) fn refusing(error: impl Fn() -> FilesOpError + Send + Sync + 'static) -> Self {
        Self {
            list_calls: Mutex::new(Vec::new()),
            listing: Box::new(move || Err(error())),
        }
    }

    /// The listings asked for so far.
    pub(crate) fn list_calls(&self) -> Vec<ListCall> {
        self.list_calls.lock().unwrap().clone()
    }
}

impl BrowseHost for FakeBrowseHost {
    fn list_as_account(
        &self,
        account: &AccountName,
        path: &BrowsePath,
        window: &ListingWindow,
    ) -> Result<DirectoryListing, FilesOpError> {
        self.list_calls.lock().unwrap().push(ListCall {
            account: account.as_str().to_owned(),
            path: path.components().join("/"),
            window: *window,
        });
        (self.listing)()
    }

    fn open_for_read_as_account(
        &self,
        _account: &AccountName,
        _path: &BrowsePath,
    ) -> Result<OpenedForRead, FilesOpError> {
        Err(FilesOpError::NotARegularFile)
    }

    fn search_as_account(
        &self,
        _account: &AccountName,
        _input: &SearchInput,
    ) -> Result<SearchOutcome, FilesOpError> {
        Err(FilesOpError::DirectoryUnusable)
    }
}
