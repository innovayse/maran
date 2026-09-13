//! Turning a removal request into a validated input, or refusing it.

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::files::DeleteEntryInput;

use crate::proto::{AgentError, DeleteEntryRequest};
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds a validated removal from what the panel sent.
///
/// Three checks, and the third is the one worth reading. `files.proto` declares
/// a `recursive` flag and the agent does not implement it, so a request that
/// sets it is REFUSED rather than quietly carried out as a single-file removal.
/// Silently ignoring the flag is the dangerous reading of the two: a caller that
/// asked for a directory tree to go away and was told "done" will proceed on the
/// belief that it did.
///
/// # Errors
///
/// Returns the wire error when the account name or the path is invalid, or when
/// the request asks for a recursive removal.
pub fn validated_delete(request: &DeleteEntryRequest) -> Result<DeleteEntryInput, AgentError> {
    let account = AccountName::parse(&request.account_username)
        .map_err(|error| invalid_input(error.to_string()))?;
    let path =
        RelativePath::parse(&request.path).map_err(|error| invalid_input(error.to_string()))?;

    if request.recursive {
        return Err(invalid_input(
            "recursive removal is not implemented; this rpc removes one file".to_owned(),
        ));
    }

    Ok(DeleteEntryInput { account, path })
}
