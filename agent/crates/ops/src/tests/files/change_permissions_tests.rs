//! ChangePermissions hands the request to the host and returns its report.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::system::name::AccountName;

use super::change_permissions;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::change_permissions_input::ChangePermissionsInput;

fn input() -> ChangePermissionsInput {
    ChangePermissionsInput {
        account: AccountName::parse("alice").unwrap(),
        path: mutable("docs"),
        mode: FileMode::parse(0o644).unwrap(),
        directory_mode: FileMode::parse(0o755).unwrap(),
        recursive: true,
    }
}

#[test]
fn the_report_of_the_host_is_returned_unchanged() {
    let host = FakeFilesHost::new();

    let report = change_permissions(&host, &input()).unwrap();

    assert_eq!((report.changed, report.skipped), (3, 1));
    assert_eq!(host.calls(), vec![Call::ChangePermissions]);
}

#[test]
fn the_bulk_bound_comes_back_as_saturated() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::Saturated);

    assert_eq!(
        change_permissions(&host, &input()).unwrap_err(),
        FilesOpError::Saturated
    );
}
