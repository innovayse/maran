//! Which page of a directory, in which order, a listing returns.

use super::directory_sort::DirectorySort;
use super::listing_window_error::ListingWindowError;

/// Page size used when the request asks for 0.
pub const DEFAULT_LIMIT: u32 = 200;

/// Largest page one listing may return.
pub const MAXIMUM_LIMIT: u32 = 1_000;

/// The paging, ordering and hidden-file choices of one listing.
///
/// Applied in the agent so the panel never needs the whole directory: the child
/// reads and filters, the parent sorts and cuts the page.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ListingWindow {
    /// Entries skipped, in the chosen order.
    offset: u32,
    /// Entries returned at most; never 0 and never above [`MAXIMUM_LIMIT`].
    limit: u32,
    /// Whether names starting with `.` are listed.
    include_hidden: bool,
    /// The sort key.
    sort: DirectorySort,
    /// Whether the order is reversed (directories stay first).
    descending: bool,
}

impl ListingWindow {
    /// Builds a window; a `limit` of 0 means [`DEFAULT_LIMIT`].
    ///
    /// # Errors
    ///
    /// Returns [`ListingWindowError::LimitTooLarge`] for a limit above [`MAXIMUM_LIMIT`].
    pub fn new(
        offset: u32,
        limit: u32,
        include_hidden: bool,
        sort: DirectorySort,
        descending: bool,
    ) -> Result<Self, ListingWindowError> {
        if limit > MAXIMUM_LIMIT {
            return Err(ListingWindowError::LimitTooLarge);
        }
        Ok(Self {
            offset,
            limit: if limit == 0 { DEFAULT_LIMIT } else { limit },
            include_hidden,
            sort,
            descending,
        })
    }

    /// Entries skipped before the page starts.
    #[must_use]
    pub fn offset(&self) -> u32 {
        self.offset
    }

    /// Entries in the page at most.
    #[must_use]
    pub fn limit(&self) -> u32 {
        self.limit
    }

    /// Whether hidden entries are listed.
    #[must_use]
    pub fn include_hidden(&self) -> bool {
        self.include_hidden
    }

    /// The sort key.
    #[must_use]
    pub fn sort(&self) -> DirectorySort {
        self.sort
    }

    /// Whether the order is reversed.
    #[must_use]
    pub fn descending(&self) -> bool {
        self.descending
    }
}
