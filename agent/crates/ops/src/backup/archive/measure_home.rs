//! How large the account's home is, counted the way `tar` will read it.

use std::fs::read_dir;
use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;

/// Measures the account's home, in bytes.
///
/// The number goes into the manifest, where it is what an operator compares a
/// restored account against, so it has to count the same tree `tar` counts —
/// which is why this walk mirrors two of the archiver's flags rather than being
/// a generic directory size:
///
/// - **Nothing is followed.** Every entry is stated with `symlink_metadata`, so
///   a link is counted as the link it is and is never descended into. Following
///   them would make the size of an account with `secrets -> /etc` a number
///   that includes files this walk has no business reading, and it is the same
///   mistake `--dereference` would be for the archive itself.
/// - **The walk stays on one filesystem.** A directory whose device differs
///   from the home's own is a mount point and is not descended into, which is
///   what `--one-file-system` does for `tar`. This product bind-mounts the SFTP
///   jail under the home, so without this the jail's contents are counted twice.
///
/// A directory that cannot be read is a failure and not a zero. A measurement
/// that silently omits an unreadable subtree is a smaller number that reads
/// exactly like a smaller home.
///
/// Iterative rather than recursive, over an explicit stack: the depth of a
/// customer's home is a customer's decision, and a recursive walk of a
/// thousand-deep tree is a root daemon with a blown stack.
///
/// # Errors
///
/// Returns [`BackupError::HomeUnreadable`] when the home or any directory
/// beneath it cannot be read.
pub(crate) fn measure_home_bytes(home: &Path) -> Result<u64, BackupError> {
    let root = home
        .symlink_metadata()
        .map_err(|_| BackupError::HomeUnreadable)?;
    if !root.is_dir() {
        return Err(BackupError::HomeUnreadable);
    }

    let device = root.dev();
    let mut pending = vec![home.to_path_buf()];
    let mut total: u64 = 0;

    while let Some(directory) = pending.pop() {
        let entries = read_dir(&directory).map_err(|_| BackupError::HomeUnreadable)?;
        for entry in entries {
            let entry = entry.map_err(|_| BackupError::HomeUnreadable)?;
            let metadata = entry
                .path()
                .symlink_metadata()
                .map_err(|_| BackupError::HomeUnreadable)?;

            if metadata.is_dir() {
                if metadata.dev() == device {
                    pending.push(entry.path());
                }
                continue;
            }

            // Symlinks and every other kind of entry contribute their own size
            // and nothing they point at.
            total = total.saturating_add(metadata.len());
        }
    }

    Ok(total)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/measure_home_tests.rs"]
mod tests;
