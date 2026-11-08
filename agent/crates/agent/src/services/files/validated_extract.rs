//! Turning an extraction request into a validated input, or refusing it.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;
use maran_ops::files::model::extract_archive_input::ExtractArchiveInput;

use crate::proto::{AgentError, ExtractArchiveRequest};
use crate::services::files::refuse_reserved_path::refuse_reserved_path;
use crate::services::files::validated_archive_format::validated_archive_format;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated extraction from what the panel sent.
///
/// The caps travel in the request and become an [`ExtractBounds`]: a cap of zero
/// or one above the agent's ceiling is refused, so an extraction always runs
/// under a stated allowance and `LIMIT_REACHED` can only mean that allowance was
/// reached. The archive path may not be the home (a directory is not an
/// archive); the destination may, which is "extract here". Neither may name the
/// account's trash.
///
/// # Errors
///
/// Returns the wire error when the account name or either path is invalid, when
/// the archive path is empty, when the format is unspecified or unknown, or when
/// a cap is zero or above the agent's ceiling.
pub fn validated_extract(
    request: &ExtractArchiveRequest,
) -> Result<ExtractArchiveInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let archive = BrowsePath::parse(&request.archive_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    if archive.is_home() {
        return Err(invalid_input("the archive path names no file".to_owned()));
    }
    let destination = BrowsePath::parse(&request.destination_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    refuse_reserved_path(&archive)?;
    refuse_reserved_path(&destination)?;
    let format = validated_archive_format(request.format)?;
    let bounds = ExtractBounds::parse(
        request.max_entries,
        request.max_expanded_bytes,
        request.max_ratio,
    )
    .map_err(|error| invalid_input(error.to_string()))?;

    Ok(ExtractArchiveInput {
        account,
        archive,
        destination,
        format,
        bounds,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_extract_tests.rs"]
mod tests;
