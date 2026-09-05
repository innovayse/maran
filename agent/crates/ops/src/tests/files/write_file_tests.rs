//! Tests for the write operation's decisions: which steps, in which order, and
//! which of them is skipped when an earlier one refuses.
//!
//! **What these tests do NOT prove, said here so that nobody reads them as
//! proving it.** They drive `FakeFilesHost`, a recording mock, so they pin the
//! ORDER OF THE CALLS and nothing about what the calls do. The real filesystem
//! behaviour of every step lives in `open_parent_directory_tests`,
//! `write_in_home_tests` and the polygon suite, and the containment this
//! operation relies on is a property of the descriptor walk rather than of any
//! call this file can see. There is no `resolve_in_home` here to assert on:
//! review established it could not fail on the write path, and it was deleted
//! rather than kept as decoration (see the note on `write_file`).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use super::write_file;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::model::write_file_input::WriteFileInput;

/// The challenge path a real ACME issuance asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// A write of `contents` at the challenge path with `mode`.
///
/// `mode` is a [`FileMode`], so there is no "a bad mode is refused" test in this
/// file any more and there cannot be one: the refusal moved into the type, where
/// `file_mode_tests` drives it one bit at a time. What is left here is what this
/// operation decides — which host calls, in which order.
fn input(contents: &[u8], mode: u32) -> WriteFileInput {
    WriteFileInput {
        account: AccountName::parse("acme").unwrap(),
        path: RelativePath::parse(CHALLENGE).unwrap(),
        contents: contents.to_vec(),
        mode: FileMode::parse(mode).unwrap(),
    }
}

#[test]
fn the_directories_are_created_before_the_content_is_written_and_nothing_else_happens() {
    let host = FakeFilesHost::new();

    let written = write_file(&host, &input(b"token.key", 0o644)).unwrap();

    assert_eq!(written, 9);
    // The exact sequence, not a subset: an operation that grew a third step
    // would fail here and have to justify it, which is how the `resolve_in_home`
    // that used to sit between these two came to be examined at all.
    assert_eq!(
        host.calls(),
        vec![
            Call::CreateParents(CHALLENGE.to_owned()),
            Call::Write(CHALLENGE.to_owned(), b"token.key".to_vec(), 0o644),
        ]
    );
}

#[test]
fn the_write_asks_the_host_to_resolve_nothing_at_all() {
    let host = FakeFilesHost::new();

    write_file(&host, &input(b"token.key", 0o644)).unwrap();

    // Stated as its own test rather than left implicit in the sequence above,
    // because it is a decision and not an accident. A containment call on this
    // path could not fail — the walk starts at the home, follows no symlink and
    // traverses no `..` — and a defensive call that cannot fail is decoration a
    // later reader mistakes for protection.
    assert!(
        !host
            .calls()
            .iter()
            .any(|call| matches!(call, Call::Resolve(_))),
        "the descriptor walk is the containment; a second check here would be inert"
    );
}

#[test]
fn a_directory_chain_that_cannot_be_created_is_refused_before_anything_is_written() {
    let host = FakeFilesHost::failing_create_parents(FilesOpError::DirectoryUnusable);

    let refused = write_file(&host, &input(b"token.key", 0o644)).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert_eq!(
        host.calls(),
        vec![Call::CreateParents(CHALLENGE.to_owned())]
    );
}

#[test]
fn a_plain_permission_mode_is_passed_through_to_the_write_untouched() {
    let host = FakeFilesHost::new();

    write_file(&host, &input(b"token.key", 0o600)).unwrap();

    assert!(
        host.calls().contains(&Call::Write(
            CHALLENGE.to_owned(),
            b"token.key".to_vec(),
            0o600
        )),
        "the mode the caller asked for is the mode the file gets"
    );
}

#[test]
fn a_failed_write_is_reported_rather_than_counted_as_bytes_written() {
    let host = FakeFilesHost::failing_write(FilesOpError::WriteFailed);

    let refused = write_file(&host, &input(b"token.key", 0o644)).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
}

#[test]
fn the_byte_count_reported_is_the_length_of_the_content_the_caller_sent() {
    let host = FakeFilesHost::new();

    let written = write_file(&host, &input(&[0_u8; 4096], 0o644)).unwrap();

    assert_eq!(written, 4096);
}
