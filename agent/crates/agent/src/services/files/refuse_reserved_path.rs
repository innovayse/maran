//! Keeping an archive request away from the account's trash.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Refuses a path that names the trash directory or sits inside it.
///
/// The same rule the mutating rpcs of the file manager apply through
/// `MutablePath`: the trash is reachable only through the trash rpcs. The
/// archive rpcs cannot use `MutablePath` itself, because an extraction may
/// legitimately target the home ("extract here"), so they apply the one rule
/// that type adds beyond `BrowsePath` and no other.
///
/// # Errors
///
/// Returns the wire error when the first component of `path` is the trash
/// directory's reserved name.
pub fn refuse_reserved_path(path: &BrowsePath) -> Result<(), AgentError> {
    if path.components().first().map(String::as_str) == Some(AgentPaths::TRASH_DIRECTORY_NAME) {
        return Err(invalid_input(
            "the path names a directory reserved for the trash".to_owned(),
        ));
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/services/files/refuse_reserved_path_tests.rs"]
mod tests;
