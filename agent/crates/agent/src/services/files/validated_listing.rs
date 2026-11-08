//! Turning a listing request into a validated input, or refusing it.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_ops::files::model::directory_sort::DirectorySort;
use maran_ops::files::model::list_directory_input::ListDirectoryInput;
use maran_ops::files::model::listing_window::ListingWindow;

use crate::proto::{AgentError, DirectorySort as WireSort, ListDirectoryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated listing from what the panel sent.
///
/// A sort the contract does not know is refused and not read as the default:
/// a newer panel asking for an order this agent cannot produce would otherwise
/// be answered with a different order and no sign of it, and a page boundary
/// computed in one order is wrong in another. The page size is checked by
/// [`ListingWindow::new`], which holds the ceiling.
///
/// # Errors
///
/// Returns the wire error when the account name or the path is invalid, when the
/// sort is unknown, or when the page size is above the ceiling.
pub fn validated_listing(request: &ListDirectoryRequest) -> Result<ListDirectoryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        BrowsePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;
    let sort = match WireSort::try_from(request.sort) {
        Ok(WireSort::Name) => DirectorySort::Name,
        Ok(WireSort::Size) => DirectorySort::Size,
        Ok(WireSort::Modified) => DirectorySort::Modified,
        Err(_) => {
            return Err(invalid_input(format!(
                "unknown directory sort {}",
                request.sort
            )));
        }
    };
    let window = ListingWindow::new(
        request.offset,
        request.limit,
        request.include_hidden,
        sort,
        request.descending,
    )
    .map_err(|error| invalid_input(error.to_string()))?;

    Ok(ListDirectoryInput {
        account,
        path,
        window,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_listing_tests.rs"]
mod tests;
