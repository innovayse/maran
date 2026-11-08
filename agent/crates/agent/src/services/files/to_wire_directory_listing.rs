//! A directory listing onto its wire payload.

use maran_ops::files::model::directory_listing::DirectoryListing;

use crate::proto::ListDirectoryOk;
use crate::services::files::to_wire_listed_entry::to_wire_listed_entry;

/// Converts a finished listing page into the `ListDirectoryOk` payload.
///
/// The counts travel as the operation measured them: `total_entries` is the
/// directory's size after the hidden filter and before paging, and
/// `skipped_entries` the names that could not be represented. A client draws its
/// pager from the first and learns of invisible entries from the second, so
/// neither is derived here from the page that happens to be returned.
#[must_use]
pub fn to_wire_directory_listing(listing: DirectoryListing) -> ListDirectoryOk {
    ListDirectoryOk {
        entries: listing
            .entries
            .into_iter()
            .map(to_wire_listed_entry)
            .collect(),
        total_entries: listing.total_entries,
        skipped_entries: listing.skipped_entries,
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/to_wire_directory_listing_tests.rs"]
mod tests;
