//! Writing one new file of the fleet identity.

use std::io::Write;
use std::os::unix::fs::OpenOptionsExt;
use std::path::Path;

use super::fleet_identity_error::FleetIdentityError;

/// Creates the file at `path` with `mode`, writes `contents` and syncs it to disk.
///
/// `create_new` is `O_CREAT | O_EXCL`: it fails when anything is at `path`, a symbolic link
/// included and without following it. So a file is only ever made fresh, with the mode set at
/// creation (never written world-readable and tightened afterwards), and nothing planted in the
/// directory is written through. The caller removes an existing identity first when replacing
/// one, which is a decision it makes, not something this function does.
///
/// # Errors
///
/// - [`FleetIdentityError::Io`] when the file cannot be created (it exists, or the directory is
///   not writable), written or synced. A file that was created and then failed to be written is
///   removed again before the error is returned.
pub(super) fn write_new_identity_file(
    path: &Path,
    contents: &[u8],
    mode: u32,
) -> Result<(), FleetIdentityError> {
    let io_error = |source| FleetIdentityError::Io {
        path: path.to_path_buf(),
        source,
    };

    let mut file = std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(mode)
        .open(path)
        .map_err(io_error)?;

    let written = file.write_all(contents).and_then(|()| file.sync_all());
    if let Err(source) = written {
        // Best effort: the file is ours (create_new made it), and a half-written key left
        // behind would make the next run refuse as "already exists".
        let _ = std::fs::remove_file(path);
        return Err(io_error(source));
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/fleet/write_new_identity_file_tests.rs"]
mod tests;
