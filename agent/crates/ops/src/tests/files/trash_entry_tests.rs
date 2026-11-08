//! TrashEntry hands the request to the host and returns the identifier.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::trash_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::trash_entry_input::TrashEntryInput;

fn input() -> TrashEntryInput {
    TrashEntryInput {
        account: AccountName::parse("alice").unwrap(),
        path: mutable("a"),
    }
}

#[test]
fn the_identifier_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    let id = trash_entry(&host, &input()).unwrap();

    assert_eq!(id.as_str(), "1700000000-00000000000000aa");
    assert_eq!(host.calls(), vec![Call::Trash]);
}

#[test]
fn a_missing_source_comes_back_as_not_found() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::NotFound);

    assert_eq!(
        trash_entry(&host, &input()).unwrap_err(),
        FilesOpError::NotFound
    );
}
