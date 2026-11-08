//! Tests for the extraction operation's decisions: the floor first, then one privileged step.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::extract_archive;
use crate::files::FilesOpError;
use crate::files::fake_archive_host::{ArchiveCall, FakeArchiveHost};
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::extract_archive_input::ExtractArchiveInput;

fn input() -> ExtractArchiveInput {
    ExtractArchiveInput {
        account: AccountName::parse("alice").unwrap(),
        archive: BrowsePath::parse("uploads/site.zip").unwrap(),
        destination: BrowsePath::parse("public").unwrap(),
        format: ArchiveFormat::Zip,
        bounds: ExtractBounds::parse(100, 1000, 10).unwrap(),
    }
}

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn the_floor_is_asked_and_then_the_host_extracts_once() {
    let report = ArchiveReport {
        entries: 3,
        bytes: 99,
    };
    let host = FakeArchiveHost::answering(report);

    let answer = extract_archive(&host, &input(), &floor()).unwrap();

    assert_eq!(answer, report);
    assert_eq!(
        host.calls(),
        [
            ArchiveCall::Measure,
            ArchiveCall::Extract {
                account: "alice".to_owned(),
                archive: "uploads/site.zip".to_owned()
            }
        ]
    );
}

#[test]
fn a_filesystem_below_the_floor_refuses_before_the_host_is_asked_to_extract() {
    let host = FakeArchiveHost::answering(ArchiveReport {
        entries: 0,
        bytes: 0,
    })
    .with_available(1024);

    let error = extract_archive(&host, &input(), &floor()).unwrap_err();

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
fn exactly_the_floor_is_admitted() {
    let host = FakeArchiveHost::answering(ArchiveReport {
        entries: 1,
        bytes: 1,
    })
    .with_available(1024 * 1024);

    assert!(extract_archive(&host, &input(), &floor()).is_ok());
}

#[test]
fn the_hosts_refusal_is_the_operations_refusal() {
    let host = FakeArchiveHost::refusing(|| FilesOpError::ArchiveUnsafe);

    assert_eq!(
        extract_archive(&host, &input(), &floor()).unwrap_err(),
        FilesOpError::ArchiveUnsafe
    );
}
