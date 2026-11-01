//! The [`PanelChallengeHost`] that actually touches this machine.

use std::fs::{OpenOptions, Permissions};
use std::io::{ErrorKind, Write as _};
use std::os::unix::fs::{OpenOptionsExt as _, PermissionsExt as _};
use std::path::Path;

use maran_agent_core::utils::available_bytes::available_bytes;

use crate::ssl::panel_challenge_host::PanelChallengeHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Mode of a challenge file: the web server's workers read it, nobody else
/// needs to write it, and its content is a public, single-use proof.
const CHALLENGE_MODE: u32 = 0o644;

/// Suffix of the temporary name a challenge is written under before the rename.
const TEMPORARY_SUFFIX: &str = ".partial";

/// Writes and removes the panel's challenge files as root.
///
/// The directory is the agent's own
/// ([`AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY`](maran_agent_core::agent_paths::AgentPaths)),
/// root-owned and created by the installer, so no customer owns a component of
/// the path and there is no link a customer could plant. The write still never
/// follows a name it did not create: the content goes to a temporary name
/// opened with `O_CREAT | O_EXCL` (which refuses an existing entry, link
/// included) and is renamed over the target, which replaces a link rather than
/// writing through it.
#[derive(Debug, Default)]
pub struct ProcessPanelChallengeHost;

impl ProcessPanelChallengeHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl PanelChallengeHost for ProcessPanelChallengeHost {
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error> {
        available_bytes(directory)
    }

    fn write_challenge(&self, path: &Path, contents: &str) -> Result<(), SslOpError> {
        let mut temporary = path.as_os_str().to_owned();
        temporary.push(TEMPORARY_SUFFIX);
        let temporary = Path::new(&temporary);

        // A temporary left by a write that died is ours to clear: the name is
        // derived from a validated token and lives in a root-only directory.
        if let Err(error) = std::fs::remove_file(temporary)
            && error.kind() != ErrorKind::NotFound
        {
            return Err(challenge_write(temporary, &error));
        }

        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(CHALLENGE_MODE)
            .open(temporary)
            .map_err(|error| challenge_write(temporary, &error))?;
        // Set explicitly and not left to `mode()`, which the process umask
        // narrows: the agent's unit runs under `UMask=0027`, which would leave
        // the file `0640` and unreadable to the web server's workers.
        file.set_permissions(Permissions::from_mode(CHALLENGE_MODE))
            .map_err(|error| challenge_write(temporary, &error))?;
        file.write_all(contents.as_bytes())
            .and_then(|()| file.sync_all())
            .map_err(|error| challenge_write(temporary, &error))?;

        std::fs::rename(temporary, path).map_err(|error| challenge_write(path, &error))
    }

    fn remove_challenge(&self, path: &Path) -> Result<(), SslOpError> {
        match std::fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == ErrorKind::NotFound => Err(SslOpError::ChallengeNotFound),
            Err(error) => Err(challenge_write(path, &error)),
        }
    }
}

/// Builds the write failure for `path`, naming the path and the io error only.
fn challenge_write(path: &Path, error: &std::io::Error) -> SslOpError {
    SslOpError::ChallengeWrite {
        reason: format!("{}: {error}", path.display()),
    }
}

#[cfg(test)]
#[path = "../tests/ssl/process_panel_challenge_host_tests.rs"]
mod tests;
