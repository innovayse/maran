//! Tests for the `ensure_traversable_directory` module.
//!
//! UNOBSERVED HERE: the agent's `UMask=0027`. Setting a umask needs `unsafe`,
//! which this crate forbids, so the creation case below only exercises the
//! umask of whoever runs the suite. The case that fails without the fix under
//! ANY umask is the repair of a level an earlier release left narrower; the
//! creation case is observed under 0027 by running the suite as
//! `(umask 027; cargo test -p maran-ops ensure_traversable)`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;

use super::ensure_traversable_directory;

/// The permission bits of `path`.
fn mode_of(path: &Path) -> u32 {
    std::fs::metadata(path).unwrap().permissions().mode() & 0o7777
}

/// A home at 0750, as account creation leaves it.
fn home_in(root: &Path) -> std::path::PathBuf {
    let home = root.join("acct");
    std::fs::create_dir(&home).unwrap();
    std::fs::set_permissions(&home, std::fs::Permissions::from_mode(0o750)).unwrap();
    home
}

/// Created levels are 0755 and the home is not touched.
#[test]
fn created_levels_are_world_traversable_and_the_home_stays_private() {
    let root = tempfile::tempdir().unwrap();
    let home = home_in(root.path());
    let docroot = home.join("sites/example.com");

    ensure_traversable_directory(&home, &docroot).unwrap();

    assert_eq!(mode_of(&home.join("sites")), 0o755);
    assert_eq!(mode_of(&docroot), 0o755);
    assert_eq!(mode_of(&home), 0o750);
}

/// Levels an earlier release left at 0750 are repaired, and the call is repeatable.
#[test]
fn levels_left_narrow_by_an_earlier_release_are_repaired() {
    let root = tempfile::tempdir().unwrap();
    let home = home_in(root.path());
    let docroot = home.join("sites/example.com");
    std::fs::create_dir_all(&docroot).unwrap();
    for level in [home.join("sites"), docroot.clone()] {
        std::fs::set_permissions(&level, std::fs::Permissions::from_mode(0o750)).unwrap();
    }

    ensure_traversable_directory(&home, &docroot).unwrap();
    ensure_traversable_directory(&home, &docroot).unwrap();

    assert_eq!(mode_of(&home.join("sites")), 0o755);
    assert_eq!(mode_of(&docroot), 0o755);
    assert_eq!(mode_of(&home), 0o750);
}

/// A level already wider than 0755 is not narrowed.
#[test]
fn a_wider_existing_level_is_left_alone() {
    let root = tempfile::tempdir().unwrap();
    let home = home_in(root.path());
    let docroot = home.join("sites/example.com");
    std::fs::create_dir_all(&docroot).unwrap();
    std::fs::set_permissions(&docroot, std::fs::Permissions::from_mode(0o775)).unwrap();

    ensure_traversable_directory(&home, &docroot).unwrap();

    assert_eq!(mode_of(&docroot), 0o775);
}

/// A symlink at a level, and a path outside the home, are refused.
#[test]
fn a_symlink_level_and_a_path_outside_the_home_are_refused() {
    let root = tempfile::tempdir().unwrap();
    let home = home_in(root.path());
    let elsewhere = root.path().join("elsewhere");
    std::fs::create_dir(&elsewhere).unwrap();
    std::os::unix::fs::symlink(&elsewhere, home.join("sites")).unwrap();

    assert!(ensure_traversable_directory(&home, &home.join("sites/example.com")).is_err());
    assert!(ensure_traversable_directory(&home, &elsewhere).is_err());
    assert!(ensure_traversable_directory(&home, &home).is_err());
}
