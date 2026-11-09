//! ForceDeleteEntry hands the request to the host and returns the count.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::force_delete_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::force_delete_entry_input::ForceDeleteEntryInput;

fn input() -> ForceDeleteEntryInput {
    ForceDeleteEntryInput {
        account: AccountName::parse("alice").unwrap(),
        path: mutable("t"),
    }
}

#[test]
fn the_count_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    assert_eq!(force_delete_entry(&host, &input()), Ok(7));
    assert_eq!(host.calls(), vec![Call::ForceDelete]);
}

#[test]
fn a_path_that_is_gone_comes_back_as_not_found() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::NotFound);

    assert_eq!(
        force_delete_entry(&host, &input()),
        Err(FilesOpError::NotFound)
    );
}
