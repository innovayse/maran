//! Turning a trash-restore request into a validated input, or refusing it.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_ops::files::model::restore_trash_entry_input::RestoreTrashEntryInput;

use crate::proto::{AgentError, RestoreTrashEntryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated restore from what the panel sent.
///
/// The trash id is parsed into a [`TrashEntryId`], which holds only the shape
/// the agent itself generates, so an id cannot name anything but a direct child
/// of the trash's entries directory. An empty `destination_path` is the
/// contract's "restore to the original place" and becomes `None`; a stated one
/// is a [`MutablePath`].
///
/// # Errors
///
/// Returns the wire error when the account name, the trash id or the stated
/// destination is invalid.
pub fn validated_restore(
    request: &RestoreTrashEntryRequest,
) -> Result<RestoreTrashEntryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let id =
        TrashEntryId::parse(&request.trash_id).map_err(|error| invalid_input(error.to_string()))?;
    let destination = match request.destination_path.as_str() {
        "" => None,
        stated => {
            Some(MutablePath::parse(stated).map_err(|error| invalid_input(error.to_string()))?)
        }
    };

    Ok(RestoreTrashEntryInput {
        account,
        id,
        destination,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_restore_tests.rs"]
mod tests;
