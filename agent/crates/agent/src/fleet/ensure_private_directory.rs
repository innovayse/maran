//! Creating the fleet directory when it is absent, and requiring it to be private either way.

use std::os::unix::fs::DirBuilderExt;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;

use super::fleet_identity_error::FleetIdentityError;
use super::require_private_directory::require_private_directory;

/// Makes sure `path` is a private directory owned by `owner_uid`, creating it (mode `0700`,
/// not recursive) when nothing is there.
///
/// Not recursive on purpose: the parent (`/etc/maran`) is the installer's, and a typo in the
/// flag must not make root create a deep tree of directories somewhere. An existing directory is
/// checked exactly as a read checks it and is never changed, so a loose one is an error rather
/// than something fixed behind the operator's back.
///
/// # Errors
///
/// - Every error of [`require_private_directory`] for a directory that already exists.
/// - [`FleetIdentityError::Io`] when the directory cannot be created (the parent is missing,
///   or is not writable).
pub(super) fn ensure_private_directory(
    path: &Path,
    owner_uid: u32,
) -> Result<(), FleetIdentityError> {
    match std::fs::symlink_metadata(path) {
        Ok(_) => {}
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            let created = std::fs::DirBuilder::new()
                .recursive(false)
                .mode(AgentPaths::FLEET_DIRECTORY_MODE)
                .create(path);
            // Another process creating it between the check and here is not an error to
            // hide: the check below sees whatever is there and judges it.
            match created {
                Ok(()) => {}
                Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(source) => {
                    return Err(FleetIdentityError::Io {
                        path: path.to_path_buf(),
                        source,
                    });
                }
            }
        }
        Err(source) => {
            return Err(FleetIdentityError::Io {
                path: path.to_path_buf(),
                source,
            });
        }
    }

    require_private_directory(path, owner_uid)
}

#[cfg(test)]
#[path = "../tests/fleet/ensure_private_directory_tests.rs"]
mod tests;
