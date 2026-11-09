//! ListTrash hands the account to the host and returns the listing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::list_trash;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};

#[test]
fn the_listing_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    let entries = list_trash(&host, &AccountName::parse("alice").unwrap()).unwrap();

    assert_eq!(entries.len(), 1);
    assert_eq!(entries[0].original_path, "a/b");
    assert_eq!(host.calls(), vec![Call::ListTrash]);
}

#[test]
fn a_failure_of_the_host_comes_back() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::NotFound);

    assert_eq!(
        list_trash(&host, &AccountName::parse("alice").unwrap()).unwrap_err(),
        FilesOpError::NotFound
    );
}
