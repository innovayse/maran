//! Tests for sorting and paging a directory's entries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::page_listing;
use crate::files::model::directory_sort::DirectorySort;
use crate::files::model::entry_kind::EntryKind;
use crate::files::model::listed_entries::ListedEntries;
use crate::files::model::listed_entry::ListedEntry;
use crate::files::model::listing_window::ListingWindow;

fn entry(name: &str, kind: EntryKind, size: u64, modified_unix: i64) -> ListedEntry {
    ListedEntry {
        name: name.to_owned(),
        kind,
        size,
        mode: 0o644,
        modified_unix,
        uid: 1000,
        gid: 1000,
        link_count: 1,
    }
}

fn listed() -> ListedEntries {
    ListedEntries {
        entries: vec![
            entry("b.txt", EntryKind::File, 30, 200),
            entry("zdir", EntryKind::Directory, 4096, 100),
            entry("A.txt", EntryKind::File, 10, 300),
            entry("adir", EntryKind::Directory, 4096, 400),
            entry("c.txt", EntryKind::File, 20, 100),
        ],
        skipped: 3,
    }
}

fn names(window: ListingWindow) -> Vec<String> {
    page_listing(listed(), &window)
        .entries
        .into_iter()
        .map(|entry| entry.name)
        .collect()
}

fn window(sort: DirectorySort, descending: bool, offset: u32, limit: u32) -> ListingWindow {
    ListingWindow::new(offset, limit, true, sort, descending).unwrap()
}

#[test]
fn name_order_lists_directories_first_and_ignores_case() {
    assert_eq!(
        names(window(DirectorySort::Name, false, 0, 0)),
        ["adir", "zdir", "A.txt", "b.txt", "c.txt"]
    );
}

#[test]
fn descending_reverses_each_group_but_keeps_directories_first() {
    assert_eq!(
        names(window(DirectorySort::Name, true, 0, 0)),
        ["zdir", "adir", "c.txt", "b.txt", "A.txt"]
    );
}

#[test]
fn size_order_sorts_files_by_size() {
    assert_eq!(
        names(window(DirectorySort::Size, false, 0, 0)),
        ["adir", "zdir", "A.txt", "c.txt", "b.txt"]
    );
}

#[test]
fn modified_order_sorts_by_time_and_breaks_ties_on_the_name() {
    // Directories by time (zdir 100, adir 400), then files (c 100, b 200, A 300).
    assert_eq!(
        names(window(DirectorySort::Modified, false, 0, 0)),
        ["zdir", "adir", "c.txt", "b.txt", "A.txt"]
    );
    let tied = ListedEntries {
        entries: vec![
            entry("b", EntryKind::File, 1, 5),
            entry("a", EntryKind::File, 1, 5),
        ],
        skipped: 0,
    };
    let page = page_listing(tied, &window(DirectorySort::Size, false, 0, 0));
    assert_eq!(page.entries[0].name, "a");
}

#[test]
fn the_page_is_cut_by_offset_and_limit_and_the_total_is_the_whole_directory() {
    let page = page_listing(listed(), &window(DirectorySort::Name, false, 1, 2));

    let page_names: Vec<_> = page.entries.iter().map(|e| e.name.as_str()).collect();
    assert_eq!(page_names, ["zdir", "A.txt"]);
    assert_eq!(page.total_entries, 5);
    assert_eq!(page.skipped_entries, 3);
}

#[test]
fn an_offset_past_the_end_gives_an_empty_page_with_the_true_total() {
    let page = page_listing(listed(), &window(DirectorySort::Name, false, 99, 10));

    assert!(page.entries.is_empty());
    assert_eq!(page.total_entries, 5);
}

#[test]
fn consecutive_pages_neither_repeat_nor_lose_an_entry() {
    let mut seen = Vec::new();
    for offset in [0, 2, 4] {
        seen.extend(names(window(DirectorySort::Name, false, offset, 2)));
    }

    assert_eq!(seen, ["adir", "zdir", "A.txt", "b.txt", "c.txt"]);
}
