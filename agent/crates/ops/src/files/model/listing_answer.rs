//! What the dropped child tells the parent about a directory read.

use super::listed_entries::ListedEntries;

/// The child's complete answer. A refusal travels as data and not as an exit
/// status because the exit status cannot say WHY, and "directory too large" must
/// be told apart from "directory unusable".
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ListingAnswer {
    /// The directory was read.
    Entries(ListedEntries),
    /// The directory held more entries than one listing reads.
    TooLarge,
}
