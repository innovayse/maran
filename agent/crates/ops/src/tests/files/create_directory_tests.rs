//! CreateDirectory hands the request to the host and returns its answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::system::name::AccountName;

use super::create_directory;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::create_directory_input::CreateDirectoryInput;

fn input() -> CreateDirectoryInput {
    CreateDirectoryInput {
        account: AccountName::parse("alice").unwrap(),
        path: mutable("docs"),
        mode: FileMode::parse(0o755).unwrap(),
    }
}

#[test]
fn the_host_is_asked_once_to_create_the_directory() {
    let host = FakeFilesHost::new();

    create_directory(&host, &input()).unwrap();

    assert_eq!(host.calls(), vec![Call::CreateDirectory]);
}

#[test]
fn a_taken_name_comes_back_as_already_exists() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::AlreadyExists);

    assert_eq!(
        create_directory(&host, &input()),
        Err(FilesOpError::AlreadyExists)
    );
}
