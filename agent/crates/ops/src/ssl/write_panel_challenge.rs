//! WritePanelChallenge: the file that proves control of the panel's own name.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::web::acme_key_authorization::AcmeKeyAuthorization;
use maran_agent_core::validation::web::acme_token::AcmeToken;

use crate::ssl::panel_challenge_host::PanelChallengeHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Writes `key_authorization` as the challenge file for `token`, in the
/// directory the panel's port-80 block serves `/.well-known/acme-challenge/`
/// from (issue #132).
///
/// The free-space floor comes first, before anything is created, exactly as a
/// customer's challenge written through `WriteFile` meets it (issue #148): a
/// challenge is a write like any other, and a host below its floor refuses it
/// with `ERROR_CODE_BELOW_FREE_SPACE_FLOOR` rather than letting the panel's own
/// issuance be the one write that fills the disk. The issuance then fails with
/// that code and the panel keeps the certificate it has.
///
/// Both inputs are validated types, so the file name is base64url — no `.`, no
/// `/` — and cannot leave the directory, and the content is the token, a dot
/// and base64url. Idempotent: writing the same token again replaces the file.
///
/// # Errors
///
/// Returns [`SslOpError::FreeSpace`] when the filesystem is below the floor or
/// cannot be measured, before anything is written, and
/// [`SslOpError::ChallengeWrite`] when the file cannot be written.
pub fn write_panel_challenge(
    host: &dyn PanelChallengeHost,
    floor: &FreeSpaceFloor,
    token: &AcmeToken,
    key_authorization: &AcmeKeyAuthorization,
) -> Result<(), SslOpError> {
    let directory = Path::new(AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY);
    floor.admit(host.available_bytes(directory))?;

    host.write_challenge(&challenge_path(token), key_authorization.as_str())
}

/// The challenge file for `token`: the directory joined with the token alone.
pub(crate) fn challenge_path(token: &AcmeToken) -> PathBuf {
    Path::new(AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY).join(token.as_str())
}

#[cfg(test)]
#[path = "../tests/ssl/write_panel_challenge_tests.rs"]
mod tests;
