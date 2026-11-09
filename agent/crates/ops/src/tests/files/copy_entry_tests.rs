//! CopyEntry checks the free-space floor, then hands the request to the host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::fs::transfer_bounds::TransferBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::copy_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::home_fixture::mutable;
use crate::files::model::copy_entry_input::CopyEntryInput;

fn input() -> CopyEntryInput {
    CopyEntryInput {
        account: AccountName::parse("alice").unwrap(),
        source: mutable("a"),
        destination: mutable("b"),
        bounds: TransferBounds::parse(100, 1000).unwrap(),
    }
}

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn with_room_the_copy_is_made() {
    let host = FakeFilesHost::new();

    let report = copy_entry(&host, &input(), &floor()).unwrap();

    assert_eq!((report.entries, report.bytes), (5, 50));
    assert_eq!(host.calls(), vec![Call::Copy]);
}

#[test]
fn a_filesystem_below_the_floor_refuses_before_the_host_is_asked_to_copy() {
    let host = FakeFilesHost::with_available(1024);

    let refused = copy_entry(&host, &input(), &floor()).unwrap_err();

    assert!(matches!(
        refused,
        FilesOpError::FreeSpace(FloorRefusal::BelowFloor { .. })
    ));
    assert!(host.calls().is_empty());
}

#[test]
fn a_filesystem_exactly_at_the_floor_is_admitted() {
    let host = FakeFilesHost::with_available(1024 * 1024);

    assert!(copy_entry(&host, &input(), &floor()).is_ok());
}

#[test]
fn the_accounts_quota_ending_the_copy_comes_back_as_quota_exceeded() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::QuotaExceeded);

    assert_eq!(
        copy_entry(&host, &input(), &floor()).unwrap_err(),
        FilesOpError::QuotaExceeded
    );
}
