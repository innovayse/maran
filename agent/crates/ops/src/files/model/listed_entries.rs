//! Everything one directory read produced, before paging.

use super::listed_entry::ListedEntry;

/// The most entries one directory read keeps, and the most an answer may announce.
///
/// A directory with more is refused as too large rather than truncated: a partial
/// listing would pass for the whole directory.
pub(crate) const MAXIMUM_LISTED_ENTRIES: usize = 100_000;

/// The entries of a directory after the hidden filter, and how many names could
/// not be represented.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ListedEntries {
    /// The entries, in no particular order.
    pub entries: Vec<ListedEntry>,
    /// Names skipped because they are not UTF-8 or hold a control character:
    /// no later request could address them.
    pub skipped: u32,
}
