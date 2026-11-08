//! MoveEntry hands the request to the host and returns its answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::move_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::move_entry_input::MoveEntryInput;

fn input() -> MoveEntryInput {
    MoveEntryInput {
        account: AccountName::parse("alice").unwrap(),
        source: mutable("a"),
        destination: mutable("b"),
    }
}

#[test]
fn the_host_is_asked_once_to_move_the_entry() {
    let host = FakeFilesHost::new();

    move_entry(&host, &input()).unwrap();

    assert_eq!(host.calls(), vec![Call::Move]);
}

#[test]
fn a_missing_source_comes_back_as_not_found() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::NotFound);

    assert_eq!(move_entry(&host, &input()), Err(FilesOpError::NotFound));
}
