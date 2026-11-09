//! Tests for `to_wire_directory_listing`: the counts are the operation's, not
//! derived from the page.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::directory_listing::DirectoryListing;
use maran_ops::files::model::entry_kind::EntryKind;
use maran_ops::files::model::listed_entry::ListedEntry;

use super::to_wire_directory_listing;

/// A file entry called `name`.
fn file(name: &str) -> ListedEntry {
    ListedEntry {
        name: name.to_owned(),
        kind: EntryKind::File,
        size: 1,
        mode: 0o644,
        modified_unix: 0,
        uid: 1,
        gid: 1,
        link_count: 1,
    }
}

#[test]
fn the_page_keeps_its_order_and_the_counts_travel_as_measured() {
    let wire = to_wire_directory_listing(DirectoryListing {
        entries: vec![file("b"), file("a")],
        total_entries: 4_000,
        skipped_entries: 2,
    });

    let names: Vec<_> = wire
        .entries
        .iter()
        .map(|entry| entry.name.as_str())
        .collect();
    assert_eq!(names, ["b", "a"]);
    assert_eq!(wire.total_entries, 4_000, "not the length of the page");
    assert_eq!(wire.skipped_entries, 2);
}

#[test]
fn an_empty_page_of_a_non_empty_directory_keeps_its_total() {
    let wire = to_wire_directory_listing(DirectoryListing {
        entries: Vec::new(),
        total_entries: 7,
        skipped_entries: 0,
    });

    assert!(wire.entries.is_empty());
    assert_eq!(wire.total_entries, 7);
}
