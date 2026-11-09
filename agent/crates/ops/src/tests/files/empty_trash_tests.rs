//! EmptyTrash hands the account to the host and returns the count.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::empty_trash;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};

#[test]
fn the_count_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    assert_eq!(
        empty_trash(&host, &AccountName::parse("alice").unwrap()),
        Ok(2)
    );
    assert_eq!(host.calls(), vec![Call::EmptyTrash]);
}

#[test]
fn the_bulk_bound_comes_back_as_saturated() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::Saturated);

    assert_eq!(
        empty_trash(&host, &AccountName::parse("alice").unwrap()),
        Err(FilesOpError::Saturated)
    );
}
