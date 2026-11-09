//! The check that the fleet directory is as private as it must be.

use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::path::Path;

use super::fleet_identity_error::FleetIdentityError;

/// Requires `path` to be a plain directory, owned by `owner_uid`, with no access for group or
/// others.
///
/// Used before the agent READS the node key or the pinned panel certificate and before it WRITES
/// them. The directory's ownership and mode are the whole protection of the key (the files in it
/// are `0600` as well, but a directory anyone can write lets them swap a file), so a directory
/// that is looser or someone else's is refused, never repaired: silently tightening it would hide
/// that something else had been able to write there.
///
/// `owner_uid` is a parameter and not read here, so a test can use a directory it owns and a
/// root daemon passes the uid it runs as.
///
/// # Errors
///
/// - [`FleetIdentityError::DirectoryMissing`] when nothing exists at `path`.
/// - [`FleetIdentityError::NotADirectory`] when it is a file or a symbolic link.
/// - [`FleetIdentityError::DirectoryOwnedByAnother`] when its owner is not `owner_uid`.
/// - [`FleetIdentityError::DirectoryTooPermissive`] when any group or other bit is set.
/// - [`FleetIdentityError::Io`] when the status cannot be read for another reason.
pub(super) fn require_private_directory(
    path: &Path,
    owner_uid: u32,
) -> Result<(), FleetIdentityError> {
    // symlink_metadata and not metadata: a link to a private directory is not a private
    // directory, and following it would check the target and then use the link.
    let metadata = match std::fs::symlink_metadata(path) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Err(FleetIdentityError::DirectoryMissing {
                path: path.to_path_buf(),
            });
        }
        Err(source) => {
            return Err(FleetIdentityError::Io {
                path: path.to_path_buf(),
                source,
            });
        }
    };

    if !metadata.is_dir() {
        return Err(FleetIdentityError::NotADirectory {
            path: path.to_path_buf(),
        });
    }

    if metadata.uid() != owner_uid {
        return Err(FleetIdentityError::DirectoryOwnedByAnother {
            path: path.to_path_buf(),
            owner: metadata.uid(),
            expected: owner_uid,
        });
    }

    let mode = metadata.permissions().mode() & 0o777;
    if mode & 0o077 != 0 {
        return Err(FleetIdentityError::DirectoryTooPermissive {
            path: path.to_path_buf(),
            mode,
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/fleet/require_private_directory_tests.rs"]
mod tests;
