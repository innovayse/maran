//! Tests for the removal operation's decisions.
//!
//! Two things are pinned here and they are the two the forked child cannot
//! decide for itself: that containment is proved BEFORE anything is unlinked,
//! and that "already gone" is answered by the root-side check rather than by
//! reading a child's failure as an absence.
//!
//! These drive `FakeFilesHost`, so — as in `write_file_tests` — what they prove
//! is the SHAPE of the operation and not the behaviour of the calls. The one
//! refusal the root-side `resolve_in_home` is genuinely the only producer of,
//! the idempotent `NotFound`, is driven against a real filesystem and a real
//! privilege drop in the polygon
//! (`a_challenge_that_is_already_gone_is_reported_as_not_found`).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use super::delete_entry;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::model::delete_entry_input::DeleteEntryInput;

/// The challenge path a real ACME cleanup asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// A removal of the challenge file.
fn input() -> DeleteEntryInput {
    DeleteEntryInput {
        account: AccountName::parse("acme").unwrap(),
        path: RelativePath::parse(CHALLENGE).unwrap(),
    }
}

#[test]
fn the_path_is_proved_contained_before_the_entry_is_unlinked() {
    let host = FakeFilesHost::new();

    delete_entry(&host, &input()).unwrap();

    assert_eq!(
        host.calls(),
        vec![
            Call::Resolve(CHALLENGE.into()),
            Call::Remove(CHALLENGE.to_owned()),
        ]
    );
}

#[test]
fn a_path_that_resolves_outside_the_home_is_refused_and_nothing_is_unlinked() {
    let host = FakeFilesHost::failing_resolve(FilesOpError::EscapesHome);

    let refused = delete_entry(&host, &input()).unwrap_err();

    assert_eq!(refused, FilesOpError::EscapesHome);
    assert_eq!(host.calls(), vec![Call::Resolve(CHALLENGE.into())]);
}

#[test]
fn a_file_that_is_already_gone_is_the_idempotent_not_found_and_no_removal_is_attempted() {
    let host = FakeFilesHost::failing_resolve(FilesOpError::NotFound);

    let answer = delete_entry(&host, &input()).unwrap_err();

    assert_eq!(answer, FilesOpError::NotFound);
    assert!(
        !host
            .calls()
            .iter()
            .any(|call| matches!(call, Call::Remove(_))),
        "there is nothing to unlink once the path is known to be absent"
    );
}

#[test]
fn a_removal_the_child_refuses_is_reported_as_a_refusal_and_never_as_an_absence() {
    let host = FakeFilesHost::failing_remove(FilesOpError::RemoveFailed);

    let refused = delete_entry(&host, &input()).unwrap_err();

    assert_eq!(
        refused,
        FilesOpError::RemoveFailed,
        "a child that refused a FIFO or a hardlink must not be read as \"nothing was there\""
    );
}
