//! Tests for `to_wire_listed_entry`: the contract's promise about links and
//! the carriage of every field.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::entry_kind::EntryKind;
use maran_ops::files::model::listed_entry::ListedEntry;

use super::to_wire_listed_entry;

/// An entry of `kind`, with distinct values in every field so a swapped pair shows.
fn entry(kind: EntryKind) -> ListedEntry {
    ListedEntry {
        name: "thing".to_owned(),
        kind,
        size: 11,
        mode: 0o640,
        modified_unix: 1_700_000_000,
        uid: 1001,
        gid: 1002,
        link_count: 3,
    }
}

#[test]
fn every_field_is_carried_to_its_own_wire_field() {
    let wire = to_wire_listed_entry(entry(EntryKind::File));

    assert_eq!(wire.name, "thing");
    assert_eq!(wire.size_bytes, 11);
    assert_eq!(wire.mode, 0o640);
    assert_eq!(wire.modified_at_unix, 1_700_000_000);
    assert_eq!(wire.uid, 1001);
    assert_eq!(wire.gid, 1002);
    assert_eq!(wire.link_count, 3);
}

#[test]
fn a_directory_is_a_directory_and_not_a_symlink() {
    let wire = to_wire_listed_entry(entry(EntryKind::Directory));

    assert!(wire.is_directory);
    assert!(!wire.is_symlink);
}

#[test]
fn a_symlink_is_a_symlink_and_never_a_directory() {
    let wire = to_wire_listed_entry(entry(EntryKind::Symlink));

    assert!(wire.is_symlink);
    assert!(
        !wire.is_directory,
        "a client that descends into every is_directory entry must never follow a link"
    );
}

#[test]
fn a_file_and_an_other_entry_are_neither_directory_nor_symlink() {
    for kind in [EntryKind::File, EntryKind::Other] {
        let wire = to_wire_listed_entry(entry(kind));

        assert!(!wire.is_directory, "{kind:?}");
        assert!(!wire.is_symlink, "{kind:?}");
    }
}
