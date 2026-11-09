//! What a name search inside a customer's home carries.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::search_query::SearchQuery;
use maran_agent_core::validation::system::name::AccountName;

/// Depth searched when the request asks for none.
const DEFAULT_DEPTH: u32 = 8;

/// Deepest a search goes whatever is asked.
const MAXIMUM_DEPTH: u32 = 16;

/// Results returned when the request asks for none.
const DEFAULT_RESULTS: u32 = 100;

/// Most results a search returns whatever is asked.
const MAXIMUM_RESULTS: u32 = 500;

/// One validated search request, with its bounds already clamped.
///
/// Clamped and not refused: a request for more than the agent allows gets what
/// the agent allows, and the answer says it was cut (`truncated`).
#[derive(Debug, Clone)]
pub struct SearchInput {
    /// The account whose home is searched.
    account: AccountName,
    /// The directory the search starts in; the home when empty.
    start: BrowsePath,
    /// The substring an entry's name must contain.
    query: SearchQuery,
    /// Directory levels searched below `start`: 1 searches `start` alone.
    max_depth: u32,
    /// Matches after which the search stops.
    max_results: u32,
    /// Whether names starting with `.` are searched and reported.
    include_hidden: bool,
}

impl SearchInput {
    /// Builds a request. `depth` and `results` of zero mean the default; values
    /// above the ceiling are lowered to it.
    #[must_use]
    pub fn new(
        account: AccountName,
        start: BrowsePath,
        query: SearchQuery,
        depth: u32,
        results: u32,
        include_hidden: bool,
    ) -> Self {
        let max_depth = if depth == 0 {
            DEFAULT_DEPTH
        } else {
            depth.min(MAXIMUM_DEPTH)
        };
        let max_results = if results == 0 {
            DEFAULT_RESULTS
        } else {
            results.min(MAXIMUM_RESULTS)
        };

        Self {
            account,
            start,
            query,
            max_depth,
            max_results,
            include_hidden,
        }
    }

    /// The account whose home is searched.
    #[must_use]
    pub fn account(&self) -> &AccountName {
        &self.account
    }

    /// The directory the search starts in.
    #[must_use]
    pub fn start(&self) -> &BrowsePath {
        &self.start
    }

    /// The substring an entry's name must contain.
    #[must_use]
    pub fn query(&self) -> &SearchQuery {
        &self.query
    }

    /// Directory levels searched below the start.
    #[must_use]
    pub fn max_depth(&self) -> u32 {
        self.max_depth
    }

    /// Matches after which the search stops.
    #[must_use]
    pub fn max_results(&self) -> u32 {
        self.max_results
    }

    /// Whether hidden names are searched.
    #[must_use]
    pub fn include_hidden(&self) -> bool {
        self.include_hidden
    }
}

#[cfg(test)]
#[path = "../../tests/files/model/search_input_tests.rs"]
mod tests;
