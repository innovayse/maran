//! Turning a move request into a validated input, or refusing it.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_ops::files::model::move_entry_input::MoveEntryInput;

use crate::proto::{AgentError, MoveEntryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated move from what the panel sent.
///
/// Both ends are [`MutablePath`]s: neither can be the home, and neither can be
/// the trash directory, so the move rpc cannot be used to take the trash apart
/// or to put it inside itself.
///
/// # Errors
///
/// Returns the wire error when the account name or either path is invalid.
pub fn validated_move(request: &MoveEntryRequest) -> Result<MoveEntryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let source = MutablePath::parse(&request.source_path)
        .map_err(|error| invalid_input(error.to_string()))?;
    let destination = MutablePath::parse(&request.destination_path)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok(MoveEntryInput {
        account,
        source,
        destination,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_move_tests.rs"]
mod tests;
