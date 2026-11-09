//! RestoreTrashEntry hands the request to the host and returns the restored path.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

use super::restore_trash_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::model::restore_trash_entry_input::RestoreTrashEntryInput;

fn input() -> RestoreTrashEntryInput {
    RestoreTrashEntryInput {
        account: AccountName::parse("alice").unwrap(),
        id: TrashEntryId::parse("1700000000-00000000000000aa").unwrap(),
        destination: None,
    }
}

#[test]
fn the_restored_path_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    assert_eq!(restore_trash_entry(&host, &input()), Ok("a/b".to_owned()));
    assert_eq!(host.calls(), vec![Call::Restore]);
}

#[test]
fn a_taken_destination_comes_back_as_already_exists() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::AlreadyExists);

    assert_eq!(
        restore_trash_entry(&host, &input()),
        Err(FilesOpError::AlreadyExists)
    );
}
