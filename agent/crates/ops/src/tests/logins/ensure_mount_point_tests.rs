//! The mode of a jail's mount point is set while nothing is mounted over it and
//! never once the account's home is (issue #483).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, create_dir, set_permissions};
use std::os::unix::fs::{PermissionsExt as _, symlink};
use std::path::Path;

use tempfile::tempdir;

use super::ensure_mount_point;

/// The mode of the directory at `path`, permission bits only.
fn mode_of(path: &Path) -> u32 {
    std::fs::metadata(path).unwrap().permissions().mode() & 0o777
}

/// A home at `0750`, as an account's is.
fn home_in(root: &Path) -> std::path::PathBuf {
    let home = root.join("home-of-alice");
    create_dir(&home).unwrap();
    set_permissions(&home, Permissions::from_mode(0o750)).unwrap();
    home
}

/// A mount point that does not exist yet is created and given the mode.
#[test]
fn a_missing_mount_point_is_created_with_the_requested_mode() {
    let root = tempdir().unwrap();
    let home = home_in(root.path());
    let mount_point = root.path().join("jail").join("home");

    ensure_mount_point(&mount_point, &home, 0o755).unwrap();

    assert_eq!(mode_of(&mount_point), 0o755);
    assert_eq!(mode_of(&home), 0o750, "the home was not the target");
}

/// An empty mount point whose mode drifted is brought back (the inverse
/// control: the function still sets the mode when nothing is mounted).
#[test]
fn an_unmounted_mount_point_has_its_mode_reapplied() {
    let root = tempdir().unwrap();
    let home = home_in(root.path());
    let mount_point = root.path().join("home");
    create_dir(&mount_point).unwrap();
    set_permissions(&mount_point, Permissions::from_mode(0o700)).unwrap();

    ensure_mount_point(&mount_point, &home, 0o755).unwrap();

    assert_eq!(mode_of(&mount_point), 0o755);
}

/// With the account's home standing at the mount point, its mode is left alone.
///
/// A link stands in for the bind mount: both resolve to the home's own inode,
/// which is the identity the function compares, so a `chmod` by path would
/// change the home exactly as it does through the real mount. This is the
/// planted break: the old behaviour widened `0750` to `0755`.
#[test]
fn a_mount_point_that_is_the_account_home_keeps_the_home_mode() {
    let root = tempdir().unwrap();
    let home = home_in(root.path());
    let mount_point = root.path().join("home");
    symlink(&home, &mount_point).unwrap();

    ensure_mount_point(&mount_point, &home, 0o755).unwrap();
    ensure_mount_point(&mount_point, &home, 0o755).unwrap();

    assert_eq!(
        mode_of(&home),
        0o750,
        "a second call must not widen the home that is mounted over the mount point"
    );
}

/// A source that cannot be inspected is an error, never a guess that nothing is
/// mounted.
#[test]
fn an_unreadable_home_is_an_error_and_changes_nothing() {
    let root = tempdir().unwrap();
    let mount_point = root.path().join("home");
    create_dir(&mount_point).unwrap();
    set_permissions(&mount_point, Permissions::from_mode(0o700)).unwrap();

    let refused = ensure_mount_point(&mount_point, &root.path().join("absent"), 0o755);

    assert!(refused.is_err());
    assert_eq!(mode_of(&mount_point), 0o700);
}
