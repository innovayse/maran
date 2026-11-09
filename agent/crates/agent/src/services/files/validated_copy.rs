//! Turning a copy request into a validated input, or refusing it.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::transfer_bounds::TransferBounds;
use maran_ops::files::model::copy_entry_input::CopyEntryInput;

use crate::proto::{AgentError, CopyEntryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated copy from what the panel sent.
///
/// The caps travel in the request and are validated into a [`TransferBounds`]:
/// a request that states none (zero) or one above the agent's own ceiling is
/// refused, so a copy always runs under a stated allowance and
/// `LIMIT_REACHED` can only mean that allowance was reached.
///
/// # Errors
///
/// Returns the wire error when the account name or either path is invalid, or
/// when a cap is zero or above the agent's ceiling.
pub fn validated_copy(request: &CopyEntryRequest) -> Result<CopyEntryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let source = MutablePath::parse(&request.source_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    let destination = MutablePath::parse(&request.destination_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    let bounds = TransferBounds::parse(request.max_entries, request.max_bytes)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok(CopyEntryInput {
        account,
        source,
        destination,
        bounds,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_copy_tests.rs"]
mod tests;
