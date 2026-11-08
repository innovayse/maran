//! The archive key a backup rpc's destination carries, once the agent has checked it.

use maran_agent_core::validation::secrets::archive_key::ArchiveKey;

use crate::proto::{AgentError, BackupDestination};
use crate::services::wire::invalid_input::invalid_input;

/// Reads the destination's `archive_key`: `None` when the call asks for a plain
/// archive (no key), the parsed key otherwise.
///
/// An empty string is the ONE spelling of "plain". Anything else must be a whole
/// key: a key that is nearly right is refused rather than treated as absent,
/// because an absent key on creation writes a PLAIN archive where a sealed one
/// was asked for, and on a read it is a different refusal from a wrong key.
///
/// The destination itself has already been judged by
/// [`validated_destination`](crate::services::backup::validated_destination::validated_destination),
/// which every rpc that reads the key calls first; a missing destination is
/// answered there, so it is `None` here and no more is said.
///
/// # Errors
///
/// `ERROR_CODE_INVALID_INPUT` for a key that is not 64 lowercase hexadecimal
/// characters. The message names neither the key nor any part of it.
pub fn validated_archive_key(
    destination: Option<&BackupDestination>,
) -> Result<Option<ArchiveKey>, AgentError> {
    let Some(destination) = destination else {
        return Ok(None);
    };
    if destination.archive_key.is_empty() {
        return Ok(None);
    }

    ArchiveKey::parse(&destination.archive_key)
        .map(Some)
        .map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/backup/validated_archive_key_tests.rs"]
mod tests;
