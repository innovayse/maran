//! Turning a permission-change request into a validated input, or refusing it.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_ops::files::model::change_permissions_input::ChangePermissionsInput;

use crate::proto::{AgentError, ChangePermissionsRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated permission change from what the panel sent.
///
/// `directory_mode` of zero is the contract's "use `mode` for directories too",
/// resolved here once so that no layer below has to know that zero is a
/// sentinel. A recursive change that wants directories to stay traversable
/// states `directory_mode` explicitly. Both modes are [`FileMode`]s, so a
/// setuid, setgid or sticky bit is refused rather than masked.
///
/// # Errors
///
/// Returns the wire error when the account name, the path or either mode is
/// invalid.
pub fn validated_permissions(
    request: &ChangePermissionsRequest,
) -> Result<ChangePermissionsInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        MutablePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;
    let mode = FileMode::parse(request.mode).map_err(|error| invalid_input(error.to_string()))?;
    let directory_mode = match request.directory_mode {
        0 => mode,
        bits => FileMode::parse(bits).map_err(|error| invalid_input(error.to_string()))?,
    };

    Ok(ChangePermissionsInput {
        account,
        path,
        mode,
        directory_mode,
        recursive: request.recursive,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_permissions_tests.rs"]
mod tests;
