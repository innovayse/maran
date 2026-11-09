//! Turning a read request into a validated input, or refusing it.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_ops::files::model::read_file_input::ReadFileInput;

use crate::proto::{AgentError, ReadFileRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated read from what the panel sent.
///
/// The limit is part of the validation and not of the operation: `max_bytes` of
/// zero is a mistake and one above the agent's ceiling is a panel that asks for
/// more than the agent ever carries, and both are refused as invalid input
/// before a child is forked. [`ReadFileInput::new`] holds the two numbers, so
/// there is one place that knows the ceiling.
///
/// # Errors
///
/// Returns the wire error when the account name or the path is invalid, or when
/// `max_bytes` is zero or above the ceiling.
pub fn validated_read(request: &ReadFileRequest) -> Result<ReadFileInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let path =
        BrowsePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;

    ReadFileInput::new(account, path, request.max_bytes)
        .map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_read_tests.rs"]
mod tests;
