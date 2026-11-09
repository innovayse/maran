//! Turning a force-delete request into a validated input, or refusing it.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_ops::files::model::force_delete_entry_input::ForceDeleteEntryInput;

use crate::proto::{AgentError, ForceDeleteEntryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated forced removal from what the panel sent.
///
/// A [`MutablePath`], so the home itself and the trash directory can never be
/// the root of a removal that deletes a whole tree.
///
/// # Errors
///
/// Returns the wire error when the account name or the path is invalid.
pub fn validated_force_delete(
    request: &ForceDeleteEntryRequest,
) -> Result<ForceDeleteEntryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        MutablePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;

    Ok(ForceDeleteEntryInput { account, path })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_force_delete_tests.rs"]
mod tests;
