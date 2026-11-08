//! Cutting one sorted page out of a directory's entries.

use std::cmp::Ordering;

use super::directory_listing::DirectoryListing;
use super::directory_sort::DirectorySort;
use super::entry_kind::EntryKind;
use super::listed_entries::ListedEntries;
use super::listed_entry::ListedEntry;
use super::listing_window::ListingWindow;

/// Sorts `listed` by the window's key and returns the window's page.
///
/// Done in the PARENT, after the child's bytes are decoded, so the child stays
/// minimal. Directories come first whatever the key and the direction; within
/// the groups ties break on the name's bytes, so the order is total and a page
/// boundary never repeats or loses an entry between two requests.
/// `total_entries` counts the entries before the page is cut.
#[must_use]
pub(crate) fn page_listing(listed: ListedEntries, window: &ListingWindow) -> DirectoryListing {
    let ListedEntries {
        mut entries,
        skipped,
    } = listed;
    entries.sort_by(|left, right| compare(left, right, window));

    let total_entries = u32::try_from(entries.len()).unwrap_or(u32::MAX);
    let offset = usize::try_from(window.offset()).unwrap_or(usize::MAX);
    let limit = usize::try_from(window.limit()).unwrap_or(usize::MAX);
    let page = entries.into_iter().skip(offset).take(limit).collect();

    DirectoryListing {
        entries: page,
        total_entries,
        skipped_entries: skipped,
    }
}

/// Orders two entries: directories first, then the key (reversed when
/// descending), then the name.
fn compare(left: &ListedEntry, right: &ListedEntry, window: &ListingWindow) -> Ordering {
    let left_is_directory = left.kind == EntryKind::Directory;
    let right_is_directory = right.kind == EntryKind::Directory;
    // `true` sorts after `false`, so the comparison is reversed to put directories first.
    let grouping = right_is_directory.cmp(&left_is_directory);
    if grouping != Ordering::Equal {
        return grouping;
    }

    let by_key = match window.sort() {
        DirectorySort::Name => left.name.to_lowercase().cmp(&right.name.to_lowercase()),
        DirectorySort::Size => left.size.cmp(&right.size),
        DirectorySort::Modified => left.modified_unix.cmp(&right.modified_unix),
    };
    let by_key = if window.descending() {
        by_key.reverse()
    } else {
        by_key
    };

    by_key.then_with(|| left.name.as_bytes().cmp(right.name.as_bytes()))
}

#[cfg(test)]
#[path = "../../tests/files/page_listing_tests.rs"]
mod tests;
