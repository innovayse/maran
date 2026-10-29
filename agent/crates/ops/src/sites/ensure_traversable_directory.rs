//! Makes one directory inside an account's home exist with a mode nginx can traverse.

use std::os::unix::fs::{DirBuilderExt as _, PermissionsExt as _};
use std::path::Path;

/// The mode of every level of a site tree below the account home.
///
/// `0755`: the web server's worker is neither the account nor in its group at
/// these levels, and it must be able to enter `sites/` and read the document
/// root. The home itself (`<account>:<web group> 0750`) is what keeps other
/// accounts out, and it is NOT a level this module ever touches.
const SITE_TREE_MODE: u32 = 0o755;

/// Ensures `directory` and every level between it and `home` exist and carry
/// [`SITE_TREE_MODE`], leaving `home` itself alone.
///
/// The mode is set with an explicit `chmod` after each creation and not left to
/// `mkdir`'s mode argument or to `create_dir_all`, because both are narrowed by
/// the process umask: the agent's unit runs `UMask=0027`, which made `sites/`
/// and the document root `0750 account:account` and every request a 404. A level
/// that already exists with a narrower mode (created by an earlier release) is
/// repaired the same way, so the call is idempotent and converges. A level whose
/// mode already carries `0755`'s bits is left exactly as it is, so a customer's
/// wider choice is never narrowed.
///
/// A symbolic link at a level is refused rather than followed: `chmod` follows
/// links, and the call runs as the account, so the worst a link could do is
/// change a mode the account owns anyway; refusing keeps even that out.
///
/// Allocation-free by construction (recursion over `Path::parent`), because it
/// runs in a forked child where only the forking thread survived.
///
/// # Errors
///
/// Returns the underlying [`std::io::Error`] when a level cannot be created or
/// changed, when a level is a symbolic link or not a directory, and
/// `InvalidInput` when `directory` is not strictly below `home`.
pub(crate) fn ensure_traversable_directory(home: &Path, directory: &Path) -> std::io::Result<()> {
    if directory == home || !directory.starts_with(home) {
        return Err(std::io::ErrorKind::InvalidInput.into());
    }
    ensure_level(home, directory)
}

/// Handles `level`, after every level above it below `home` has been handled.
///
/// # Errors
///
/// As [`ensure_traversable_directory`].
fn ensure_level(home: &Path, level: &Path) -> std::io::Result<()> {
    if level == home {
        return Ok(());
    }
    if let Some(parent) = level.parent() {
        ensure_level(home, parent)?;
    }

    match std::fs::symlink_metadata(level) {
        Ok(metadata) if metadata.is_dir() => {
            let mode = metadata.permissions().mode() & 0o7777;
            if mode & SITE_TREE_MODE != SITE_TREE_MODE {
                std::fs::set_permissions(
                    level,
                    std::fs::Permissions::from_mode(mode | SITE_TREE_MODE),
                )?;
            }
            Ok(())
        }
        Ok(_) => Err(std::io::ErrorKind::AlreadyExists.into()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            std::fs::DirBuilder::new()
                .mode(SITE_TREE_MODE)
                .create(level)?;
            std::fs::set_permissions(level, std::fs::Permissions::from_mode(SITE_TREE_MODE))
        }
        Err(error) => Err(error),
    }
}

#[cfg(test)]
#[path = "../tests/sites/ensure_traversable_directory_tests.rs"]
mod tests;
