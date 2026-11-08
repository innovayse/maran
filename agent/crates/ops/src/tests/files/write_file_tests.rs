//! Tests for the write operation's decisions: the floor first, then one privileged step.
//!
//! **What these tests do NOT prove, said here so that nobody reads them as
//! proving it.** They drive `FakeFilesHost`, a recording mock, so they pin the
//! ORDER OF THE CALLS and nothing about what the calls do. The real filesystem
//! behaviour lives in `write_in_home_tests` and the polygon suite, and the
//! containment this operation relies on is a property of the descriptor walk
//! rather than of any call this file can see. There is no `resolve_in_home`
//! here to assert on: it could not fail on the write path, and was deleted
//! rather than kept as decoration (see the note on `write_file`).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::upload_limit::UploadLimit;
use maran_agent_core::validation::system::name::AccountName;

use super::write_file;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::model::buffered_upload::BufferedUpload;
use crate::files::model::upload_aborted::UploadAborted;
use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;

/// The challenge path a real ACME issuance asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// A write at the challenge path with `mode`, replacing, under the default cap.
fn input(mode: u32) -> WriteFileInput {
    WriteFileInput {
        account: AccountName::parse("acme").unwrap(),
        path: MutablePath::parse(CHALLENGE).unwrap(),
        mode: FileMode::parse(mode).unwrap(),
        fail_if_exists: false,
        limit: UploadLimit::parse(0).unwrap(),
    }
}

/// A one-mebibyte floor.
fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

/// A source that breaks before it finishes.
struct BrokenUpload;

impl UploadSource for BrokenUpload {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        Err(UploadAborted)
    }
}

#[test]
fn the_upload_is_handed_to_the_host_in_one_write_and_nothing_else_happens() {
    let host = FakeFilesHost::new();

    let written = write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(b"token.key".to_vec()),
        &floor(),
    )
    .unwrap();

    assert_eq!(written, 9);
    // The exact sequence, not a subset: an operation that grew a second step
    // would fail here and have to justify it.
    assert_eq!(
        host.calls(),
        vec![Call::Write(
            CHALLENGE.to_owned(),
            b"token.key".to_vec(),
            0o644,
            false,
            1_048_576
        )]
    );
}

#[test]
fn the_write_asks_the_host_to_resolve_nothing_at_all() {
    let host = FakeFilesHost::new();

    write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(b"x".to_vec()),
        &floor(),
    )
    .unwrap();

    // A decision and not an accident: a containment call on this path could not
    // fail, and a defensive call that cannot fail is decoration.
    assert!(
        !host
            .calls()
            .iter()
            .any(|call| matches!(call, Call::Resolve(_))),
        "the descriptor walk is the containment; a second check here would be inert"
    );
}

#[test]
fn the_mode_the_cap_and_the_refusal_to_replace_reach_the_host_untouched() {
    let host = FakeFilesHost::new();
    let mut refusing = input(0o600);
    refusing.fail_if_exists = true;
    refusing.limit = UploadLimit::parse(4096).unwrap();

    write_file(
        &host,
        &refusing,
        &mut BufferedUpload::new(b"x".to_vec()),
        &floor(),
    )
    .unwrap();

    assert_eq!(
        host.calls(),
        vec![Call::Write(
            CHALLENGE.to_owned(),
            b"x".to_vec(),
            0o600,
            true,
            4096
        )]
    );
}

#[test]
fn a_failed_write_is_reported_rather_than_counted_as_bytes_written() {
    let host = FakeFilesHost::failing_write(FilesOpError::WriteFailed);

    let refused = write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(b"token.key".to_vec()),
        &floor(),
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
}

#[test]
fn the_hosts_typed_refusals_pass_through_unchanged() {
    for error in [
        FilesOpError::AlreadyExists,
        FilesOpError::QuotaExceeded,
        FilesOpError::DirectoryUnusable,
    ] {
        let expected = format!("{error:?}");
        let host = FakeFilesHost::failing_write(error);

        let refused = write_file(
            &host,
            &input(0o644),
            &mut BufferedUpload::new(b"x".to_vec()),
            &floor(),
        )
        .unwrap_err();

        assert_eq!(format!("{refused:?}"), expected);
    }
}

#[test]
fn the_byte_count_reported_is_the_length_of_what_arrived() {
    let host = FakeFilesHost::new();

    let written = write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(vec![0_u8; 4096]),
        &floor(),
    )
    .unwrap();

    assert_eq!(written, 4096);
}

#[test]
fn an_upload_that_did_not_arrive_whole_is_a_failed_write_not_a_short_file() {
    let host = FakeFilesHost::new();

    let refused = write_file(&host, &input(0o644), &mut BrokenUpload, &floor()).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
}

#[test]
fn an_upload_below_the_free_space_floor_is_refused_before_anything_is_created() {
    let host = FakeFilesHost::with_available(1024 * 1024 - 1);

    let refused = write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(b"x".to_vec()),
        &floor(),
    )
    .unwrap_err();

    assert_eq!(
        refused,
        FilesOpError::FreeSpace(FloorRefusal::BelowFloor {
            available: 1024 * 1024 - 1,
            floor: 1024 * 1024,
        })
    );
    assert!(host.calls().is_empty());
}

#[test]
fn an_upload_with_exactly_the_floor_left_is_written() {
    let host = FakeFilesHost::with_available(1024 * 1024);

    let written = write_file(
        &host,
        &input(0o644),
        &mut BufferedUpload::new(b"token.key".to_vec()),
        &floor(),
    )
    .unwrap();

    assert_eq!(written, 9);
}
