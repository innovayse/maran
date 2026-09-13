//! The marker document: what it accepts back, and what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::write;

use tempfile::TempDir;

use super::*;

/// A valid account name for the fixture.
fn account() -> AccountName {
    AccountName::parse("polymarker").expect("the fixture's name is valid")
}

/// A valid backup id for the fixture.
fn backup_id() -> BackupId {
    BackupId::parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301").expect("the fixture's id is a uuid")
}

/// A marker as a restore would have written it.
fn marker() -> RestoreMarker {
    RestoreMarker {
        version: RESTORE_MARKER_VERSION,
        account: account().as_str().to_owned(),
        backup_id: backup_id().as_str().to_owned(),
        owner_uid: 1101,
        home_gid: 33,
        home_mode: 0o750,
    }
}

#[test]
fn a_written_marker_reads_back_as_the_same_document() {
    let root = TempDir::new().expect("a temporary directory");
    let path = root.path().join("written.swap");
    write(
        &path,
        serde_json::to_vec(&marker()).expect("the document encodes"),
    )
    .expect("the file");

    assert_eq!(read_restore_marker(&path).expect("it reads back"), marker());
}

#[test]
fn a_marker_names_the_account_and_the_backup_as_validated_values() {
    let marker = marker();

    assert_eq!(
        marker.account().expect("the account validates").as_str(),
        "polymarker"
    );
    assert_eq!(
        marker.backup_id().expect("the id validates").as_str(),
        "3f2504e0-4f89-41d3-9a0c-0305e82c3301"
    );
}

#[test]
fn a_marker_naming_something_that_is_not_an_account_name_yields_no_account() {
    // The load-bearing refusal: everything the reconciliation renames is built
    // from this value, so a name that would not have survived `AccountName` must
    // not survive here either. `..` is the shape that matters — it is what turns
    // a join into a path outside the home root.
    let marker = RestoreMarker {
        account: "../root".to_owned(),
        ..marker()
    };

    assert!(marker.account().is_none());
}

#[test]
fn a_marker_naming_something_that_is_not_a_backup_id_yields_no_id() {
    let marker = RestoreMarker {
        backup_id: "../../etc".to_owned(),
        ..marker()
    };

    assert!(marker.backup_id().is_none());
}

#[test]
fn a_marker_of_a_version_this_agent_does_not_know_is_refused_whole() {
    let root = TempDir::new().expect("a temporary directory");
    let path = root.path().join("future.swap");
    let future = RestoreMarker {
        version: RESTORE_MARKER_VERSION + 1,
        ..marker()
    };
    write(&path, serde_json::to_vec(&future).expect("it encodes")).expect("the file");

    // Refused rather than read field by field: acting on half a document a
    // future build wrote is a root `rename` made on a guess.
    assert!(read_restore_marker(&path).is_err());
}

#[test]
fn bytes_that_are_not_this_agents_json_are_refused() {
    let root = TempDir::new().expect("a temporary directory");
    let path = root.path().join("rubbish.swap");
    write(&path, b"not json at all").expect("the file");

    assert!(read_restore_marker(&path).is_err());
}

#[test]
fn a_marker_that_is_not_there_is_refused_rather_than_treated_as_empty() {
    let root = TempDir::new().expect("a temporary directory");

    assert!(read_restore_marker(&root.path().join("absent.swap")).is_err());
}

#[test]
fn the_marker_path_is_a_sibling_of_the_two_directories_it_describes() {
    let path = restore_marker_path(&account(), &backup_id());
    let staging = AgentPaths::restore_staging_dir(&account(), &backup_id());
    let previous = AgentPaths::restore_previous_dir(&account(), &backup_id());

    // Same directory, so the marker cannot be durable on a filesystem whose swap
    // is not, or the reverse.
    assert_eq!(path.parent(), staging.parent());
    assert_eq!(path.parent(), previous.parent());
    assert!(
        path.to_string_lossy().ends_with(MARKER_SUFFIX),
        "the marker must carry the suffix the reconciliation opens by: {}",
        path.display()
    );
}

#[test]
fn a_partial_marker_name_never_matches_a_finished_one() {
    // The reconciliation sweeps `.swap.partial` away and acts on `.swap`. If the
    // partial name did not end in the partial suffix, or the finished check saw
    // partials first, an interrupted WRITE would be read as a finished document.
    let path = restore_marker_path(&account(), &backup_id());
    let partial = path.with_extension("swap.partial");

    assert!(partial.to_string_lossy().ends_with(PARTIAL_MARKER_SUFFIX));
    assert!(!path.to_string_lossy().ends_with(PARTIAL_MARKER_SUFFIX));
}
