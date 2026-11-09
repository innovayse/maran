//! Turning an archive-creation request into a validated input, or refusing it.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_ops::files::model::create_archive_input::CreateArchiveInput;

use crate::proto::{AgentError, CreateArchiveRequest};
use crate::services::files::refuse_reserved_path::refuse_reserved_path;
use crate::services::files::validated_archive_format::validated_archive_format;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated archive creation from what the panel sent.
///
/// Every source and the destination are browse paths, none of which may name the
/// account's trash. The combinations that
/// cannot be made safely (no source, the home as a source or destination,
/// overlapping sources, a destination inside a source) are refused by
/// [`CreateArchiveInput::new`], and the caps by [`CreateBounds`]: zero, or above
/// the agent's ceiling, is refused.
///
/// # Errors
///
/// Returns the wire error when the account name or any path is invalid, when the
/// format is unspecified or unknown, when a cap is zero or above the ceiling, or
/// when the sources and destination cannot be combined.
pub fn validated_archive(request: &CreateArchiveRequest) -> Result<CreateArchiveInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let sources = request
        .source_paths
        .iter()
        .map(|path| BrowsePath::parse(path))
        .collect::<Result<Vec<_>, _>>()
        .map_err(|error| invalid_input(error.to_string()))?;
    let destination = BrowsePath::parse(&request.destination_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    for path in sources.iter().chain([&destination]) {
        refuse_reserved_path(path)?;
    }
    let format = validated_archive_format(request.format)?;
    let bounds = CreateBounds::parse(request.max_entries, request.max_source_bytes)
        .map_err(|error| invalid_input(error.to_string()))?;

    CreateArchiveInput::new(account, sources, destination, format, bounds)
        .map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_archive_tests.rs"]
mod tests;
