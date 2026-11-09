//! Reading one file of the fleet identity, bounded.

use std::io::Read;
use std::path::Path;

use super::fleet_identity_error::FleetIdentityError;

/// Reads the plain file at `path`, refusing anything larger than `limit` bytes.
///
/// The directory is private (see `require_private_directory`), so only root can have put
/// anything here; the bound is for a file that is not what it should be, not for an attacker.
/// A link is refused rather than followed, for the same reason the directory check refuses one.
///
/// # Errors
///
/// - [`FleetIdentityError::FileMissing`] when nothing exists at `path`.
/// - [`FleetIdentityError::FileNotPlain`] when it is a directory, a link or a device.
/// - [`FleetIdentityError::FileTooLarge`] when it holds more than `limit` bytes, whether that
///   showed in its size or only while reading.
/// - [`FleetIdentityError::Io`] for any other failure to open or read it.
pub(super) fn read_identity_file(path: &Path, limit: u64) -> Result<Vec<u8>, FleetIdentityError> {
    let io_error = |source| FleetIdentityError::Io {
        path: path.to_path_buf(),
        source,
    };

    let metadata = match std::fs::symlink_metadata(path) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Err(FleetIdentityError::FileMissing {
                path: path.to_path_buf(),
            });
        }
        Err(source) => return Err(io_error(source)),
    };

    if !metadata.is_file() {
        return Err(FleetIdentityError::FileNotPlain {
            path: path.to_path_buf(),
        });
    }

    if metadata.len() > limit {
        return Err(FleetIdentityError::FileTooLarge {
            path: path.to_path_buf(),
            limit,
        });
    }

    let mut contents = Vec::new();
    // One byte past the limit, so a file that grew after the size check is seen as too large
    // instead of being silently truncated at the limit.
    std::fs::File::open(path)
        .map_err(io_error)?
        .take(limit.saturating_add(1))
        .read_to_end(&mut contents)
        .map_err(io_error)?;

    if !u64::try_from(contents.len()).is_ok_and(|length| length <= limit) {
        return Err(FleetIdentityError::FileTooLarge {
            path: path.to_path_buf(),
            limit,
        });
    }

    Ok(contents)
}

#[cfg(test)]
#[path = "../tests/fleet/read_identity_file_tests.rs"]
mod tests;
