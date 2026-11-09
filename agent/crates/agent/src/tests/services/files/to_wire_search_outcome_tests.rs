//! Tests for `to_wire_search_outcome`: the path travels whole, the name is its
//! last component, and the bound flags are carried.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::entry_kind::EntryKind;
use maran_ops::files::model::search_match::SearchMatch;
use maran_ops::files::model::search_outcome::SearchOutcome;

use super::to_wire_search_outcome;

/// A match at `path`.
fn found(path: &str, kind: EntryKind) -> SearchMatch {
    SearchMatch {
        path: path.to_owned(),
        kind,
        size: 5,
        mode: 0o600,
        modified_unix: 42,
        uid: 7,
        gid: 8,
        links: 2,
    }
}

#[test]
fn a_match_keeps_its_whole_path_and_names_the_entry_by_the_last_component() {
    let wire = to_wire_search_outcome(SearchOutcome {
        matches: vec![found("sites/example.com/readme.txt", EntryKind::File)],
        truncated: false,
        skipped_entries: 0,
    });

    let only = &wire.matches[0];
    assert_eq!(only.path, "sites/example.com/readme.txt");
    let entry = only.entry.as_ref().unwrap();
    assert_eq!(entry.name, "readme.txt");
    assert_eq!(entry.size_bytes, 5);
    assert_eq!(entry.mode, 0o600);
    assert_eq!(entry.modified_at_unix, 42);
    assert_eq!(entry.uid, 7);
    assert_eq!(entry.gid, 8);
    assert_eq!(entry.link_count, 2);
}

#[test]
fn a_match_at_the_top_level_is_named_by_its_whole_path() {
    let wire = to_wire_search_outcome(SearchOutcome {
        matches: vec![found("readme.txt", EntryKind::File)],
        ..SearchOutcome::default()
    });

    assert_eq!(wire.matches[0].entry.as_ref().unwrap().name, "readme.txt");
}

#[test]
fn a_symlink_match_is_a_symlink_and_not_a_directory() {
    let wire = to_wire_search_outcome(SearchOutcome {
        matches: vec![found("a/link", EntryKind::Symlink)],
        ..SearchOutcome::default()
    });

    let entry = wire.matches[0].entry.as_ref().unwrap();
    assert!(entry.is_symlink);
    assert!(!entry.is_directory);
}

#[test]
fn truncation_and_the_skipped_count_are_carried_as_measured() {
    let wire = to_wire_search_outcome(SearchOutcome {
        matches: Vec::new(),
        truncated: true,
        skipped_entries: 9,
    });

    assert!(wire.truncated);
    assert_eq!(wire.skipped_entries, 9);
    assert!(wire.matches.is_empty());
}

#[test]
fn matches_keep_the_order_the_search_found_them_in() {
    let wire = to_wire_search_outcome(SearchOutcome {
        matches: vec![found("z/b", EntryKind::File), found("a/a", EntryKind::File)],
        ..SearchOutcome::default()
    });

    let paths: Vec<_> = wire.matches.iter().map(|m| m.path.as_str()).collect();
    assert_eq!(paths, ["z/b", "a/a"]);
}
