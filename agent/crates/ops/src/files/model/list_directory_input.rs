//! What a listing is asked for.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use super::listing_window::ListingWindow;

/// The validated request of `list_directory`.
#[derive(Debug, Clone)]
pub struct ListDirectoryInput {
    /// The account whose home is browsed.
    pub account: AccountName,
    /// The directory to list; the home when empty.
    pub path: BrowsePath,
    /// Page, order and hidden-file choice.
    pub window: ListingWindow,
}
