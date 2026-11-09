//! One page of a directory.

use super::listed_entry::ListedEntry;

/// The page a listing returns.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DirectoryListing {
    /// The entries of the requested page, in the requested order.
    pub entries: Vec<ListedEntry>,
    /// Entries in the directory after the hidden filter, before paging.
    pub total_entries: u32,
    /// Names that could not be represented and are not in `entries`.
    pub skipped_entries: u32,
}
