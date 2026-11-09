//! Turning a trash request into a validated input, or refusing it.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_ops::files::model::trash_entry_input::TrashEntryInput;

use crate::proto::{AgentError, TrashEntryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated trash move from what the panel sent.
///
/// A [`MutablePath`], so the trash directory cannot be moved into itself and
/// the home cannot be trashed.
///
/// # Errors
///
/// Returns the wire error when the account name or the path is invalid.
pub fn validated_trash(request: &TrashEntryRequest) -> Result<TrashEntryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        MutablePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;

    Ok(TrashEntryInput { account, path })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_trash_tests.rs"]
mod tests;
