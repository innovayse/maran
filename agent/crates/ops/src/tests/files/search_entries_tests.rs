//! Tests for the search operation over a host with a canned answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::search_query::SearchQuery;
use maran_agent_core::validation::system::name::AccountName;

use super::search_entries;
use crate::files::FilesOpError;
use crate::files::browse_host::BrowseHost;
use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::listing_window::ListingWindow;
use crate::files::model::opened_for_read::OpenedForRead;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;

/// A host whose search answers with `answer`, remembering whom it was asked for.
struct CannedSearchHost {
    /// What the search gives back.
    answer: std::sync::Mutex<Option<Result<SearchOutcome, FilesOpError>>>,
    /// The account name the search was asked for.
    asked: std::sync::Mutex<Option<String>>,
}

impl BrowseHost for CannedSearchHost {
    fn list_as_account(
        &self,
        _account: &AccountName,
        _path: &BrowsePath,
        _window: &ListingWindow,
    ) -> Result<DirectoryListing, FilesOpError> {
        Err(FilesOpError::NotFound)
    }

    fn open_for_read_as_account(
        &self,
        _account: &AccountName,
        _path: &BrowsePath,
    ) -> Result<OpenedForRead, FilesOpError> {
        Err(FilesOpError::NotFound)
    }

    fn search_as_account(
        &self,
        account: &AccountName,
        _input: &SearchInput,
    ) -> Result<SearchOutcome, FilesOpError> {
        *self.asked.lock().unwrap() = Some(account.as_str().to_owned());
        self.answer.lock().unwrap().take().unwrap()
    }
}

/// A request to search for `log`.
fn request() -> SearchInput {
    SearchInput::new(
        AccountName::parse("alice1").unwrap(),
        BrowsePath::parse("").unwrap(),
        SearchQuery::parse("log").unwrap(),
        0,
        0,
        false,
    )
}

#[test]
fn the_hosts_outcome_is_returned_and_the_host_was_asked_for_the_requests_account() {
    let outcome = SearchOutcome {
        matches: Vec::new(),
        truncated: true,
        skipped_entries: 4,
    };
    let host = CannedSearchHost {
        answer: std::sync::Mutex::new(Some(Ok(outcome.clone()))),
        asked: std::sync::Mutex::new(None),
    };

    assert_eq!(search_entries(&host, &request()).unwrap(), outcome);
    assert_eq!(host.asked.lock().unwrap().as_deref(), Some("alice1"));
}

#[test]
fn a_refusal_of_the_host_reaches_the_caller() {
    let host = CannedSearchHost {
        answer: std::sync::Mutex::new(Some(Err(FilesOpError::Saturated))),
        asked: std::sync::Mutex::new(None),
    };

    assert_eq!(
        search_entries(&host, &request()).unwrap_err(),
        FilesOpError::Saturated
    );
}
