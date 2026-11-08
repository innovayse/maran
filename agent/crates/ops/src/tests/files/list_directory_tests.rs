//! Tests for the listing operation's shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use super::list_directory;
use crate::files::FilesOpError;
use crate::files::fake_browse_host::FakeBrowseHost;
use crate::files::model::directory_listing::DirectoryListing;
use crate::files::model::directory_sort::DirectorySort;
use crate::files::model::list_directory_input::ListDirectoryInput;
use crate::files::model::listing_window::ListingWindow;

/// A listing request for `sites` with a recognisable window.
fn input() -> ListDirectoryInput {
    ListDirectoryInput {
        account: AccountName::parse("acme").unwrap(),
        path: BrowsePath::parse("sites").unwrap(),
        window: ListingWindow::new(5, 50, true, DirectorySort::Size, true).unwrap(),
    }
}

#[test]
fn the_host_is_asked_for_exactly_the_account_path_and_window_of_the_request() {
    let host = FakeBrowseHost::answering(DirectoryListing {
        entries: Vec::new(),
        total_entries: 7,
        skipped_entries: 2,
    });
    let request = input();

    let listing = list_directory(&host, &request).unwrap();

    let calls = host.list_calls();
    assert_eq!(calls.len(), 1);
    assert_eq!(calls[0].account, "acme");
    assert_eq!(calls[0].path, "sites");
    assert_eq!(calls[0].window, request.window);
    assert_eq!(listing.total_entries, 7);
    assert_eq!(listing.skipped_entries, 2);
}

#[test]
fn every_refusal_of_the_host_reaches_the_caller_unchanged() {
    let refusals: [fn() -> FilesOpError; 4] = [
        || FilesOpError::Saturated,
        || FilesOpError::TooLarge,
        || FilesOpError::HomeUnusable,
        || FilesOpError::DirectoryUnusable,
    ];
    for refusal in refusals {
        let host = FakeBrowseHost::refusing(refusal);

        assert_eq!(list_directory(&host, &input()).unwrap_err(), refusal());
    }
}
