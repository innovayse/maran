//! Tests for the creation operation's decisions: the floor first, then one privileged step.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::create_archive;
use crate::files::FilesOpError;
use crate::files::fake_archive_host::{ArchiveCall, FakeArchiveHost};
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;

fn input() -> CreateArchiveInput {
    CreateArchiveInput::new(
        AccountName::parse("alice").unwrap(),
        vec![BrowsePath::parse("site").unwrap()],
        BrowsePath::parse("backups/site.zip").unwrap(),
        ArchiveFormat::Zip,
        CreateBounds::parse(100, 1000).unwrap(),
    )
    .unwrap()
}

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn the_floor_is_asked_and_then_the_host_creates_once() {
    let report = ArchiveReport {
        entries: 4,
        bytes: 2048,
    };
    let host = FakeArchiveHost::answering(report);

    let answer = create_archive(&host, &input(), &floor()).unwrap();

    assert_eq!(answer, report);
    assert_eq!(
        host.calls(),
        [
            ArchiveCall::Measure,
            ArchiveCall::Create {
                account: "alice".to_owned(),
                destination: "backups/site.zip".to_owned()
            }
        ]
    );
}

#[test]
fn a_filesystem_below_the_floor_refuses_before_the_host_is_asked_to_create() {
    let host = FakeArchiveHost::answering(ArchiveReport {
        entries: 0,
        bytes: 0,
    })
    .with_available(10);

    let error = create_archive(&host, &input(), &floor()).unwrap_err();

    assert!(
        matches!(
            error,
            FilesOpError::FreeSpace(FloorRefusal::BelowFloor { .. })
        ),
        "{error:?}"
    );
    assert_eq!(host.calls(), [ArchiveCall::Measure]);
}

#[test]
fn the_hosts_refusal_is_the_operations_refusal() {
    let host = FakeArchiveHost::refusing(|| FilesOpError::AlreadyExists);

    assert_eq!(
        create_archive(&host, &input(), &floor()).unwrap_err(),
        FilesOpError::AlreadyExists
    );
}
