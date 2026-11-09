//! What a name search found.

use super::search_match::SearchMatch;

/// The result of one search.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SearchOutcome {
    /// The matches, in the order the search found them.
    pub matches: Vec<SearchMatch>,
    /// A bound stopped the search, so there may be more. Never an error.
    pub truncated: bool,
    /// Entries whose names cannot be sent (not UTF-8, or holding a control
    /// character), counted so that an invisible entry is at least accounted for.
    pub skipped_entries: u32,
}
