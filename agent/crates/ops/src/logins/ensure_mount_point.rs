//! Bringing a jail's mount point to its mode without ever touching the home
//! that is mounted over it (issue #483).

use std::fs::{Permissions, create_dir_all, metadata, set_permissions};
use std::io;
use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _};
use std::path::Path;

/// Makes sure `mount_point` exists and, when nothing is mounted over it, gives
/// it `mode`.
///
/// A jail's mount point is the target of a bind mount of the account's real
/// home. `chmod(2)` follows the path onto whatever is mounted there, so the same
/// call that is harmless on the empty directory of a first login rewrites the
/// mode of the ACCOUNT'S HOME on the next one: `0750` became `0755`, and every
/// other tenant could then traverse and read the account's files. The mode of a
/// mount point is only ever visible while the mount is down, so there is nothing
/// to apply while it is up.
///
/// **How "up" is recognised.** The mount point is the account's home exactly
/// when it is the SAME INODE as the home (`st_dev` and `st_ino`, following any
/// link). That, and not a comparison of the device with the parent's, because a
/// bind mount shares its source's filesystem: on a host whose `/home` and
/// `/var/lib` are one filesystem the two device numbers are equal whether or
/// not anything is mounted, and a check built on them would see an empty
/// directory exactly where the home is.
///
/// Nothing else mounts at this path: the mount is the account's own unit, and
/// the callers hold the account's lock, so the answer cannot change between the
/// comparison and the `chmod`.
///
/// # Errors
///
/// Returns the I/O error when the directory cannot be created, either path
/// cannot be inspected, or the mode cannot be set on a mount point nothing is
/// mounted over.
pub(crate) fn ensure_mount_point(mount_point: &Path, home: &Path, mode: u32) -> io::Result<()> {
    create_dir_all(mount_point)?;

    let target = metadata(mount_point)?;
    let source = metadata(home)?;
    if target.dev() == source.dev() && target.ino() == source.ino() {
        return Ok(());
    }

    set_permissions(mount_point, Permissions::from_mode(mode))
}

#[cfg(test)]
#[path = "../tests/logins/ensure_mount_point_tests.rs"]
mod tests;
