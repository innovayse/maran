//! Turning a search request into a validated input, or refusing it.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::search_query::SearchQuery;
use maran_ops::files::model::search_input::SearchInput;

use crate::proto::{AgentError, SearchEntriesRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated search from what the panel sent.
///
/// The depth and the result count are not refused when too large: the contract
/// says they are clamped, and [`SearchInput::new`] does the clamping, so this
/// function passes them through untouched and the one place that knows the
/// ceilings stays the one place.
///
/// # Errors
///
/// Returns the wire error when the account name, the starting path or the query
/// is invalid.
pub fn validated_search(request: &SearchEntriesRequest) -> Result<SearchInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let start =
        BrowsePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;
    let query =
        SearchQuery::parse(&request.query).map_err(|error| invalid_input(error.to_string()))?;

    Ok(SearchInput::new(
        account,
        start,
        query,
        request.max_depth,
        request.max_results,
        request.include_hidden,
    ))
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_search_tests.rs"]
mod tests;
