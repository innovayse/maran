//! Turning a create-directory request into a validated input, or refusing it.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_ops::files::model::create_directory_input::CreateDirectoryInput;

use crate::proto::{AgentError, CreateDirectoryRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated directory creation from what the panel sent.
///
/// The mode is a [`FileMode`], so setuid, setgid and sticky are refused here and
/// never masked: a caller that asked for `04755` is told no rather than handed
/// `0755`.
///
/// # Errors
///
/// Returns the wire error when the account name, the path or the mode is
/// invalid, including a path that names the home itself or the reserved trash
/// directory.
pub fn validated_create_directory(
    request: &CreateDirectoryRequest,
) -> Result<CreateDirectoryInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        MutablePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;
    let mode = FileMode::parse(request.mode).map_err(|error| invalid_input(error.to_string()))?;

    Ok(CreateDirectoryInput {
        account,
        path,
        mode,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_create_directory_tests.rs"]
mod tests;
