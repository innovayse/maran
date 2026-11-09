//! The swap of one restored directory, run against real directories as the
//! current user: what it moves, what it parks, and what it refuses to touch.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir_all, read_to_string, write};
use std::os::unix::fs::symlink;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use tempfile::TempDir;

use crate::backup::model::directory_swap::DirectorySwap;

use super::*;

/// The directory every case replaces.
const SITE: &str = "sites/shop.example.com";

/// The directories one swap works over.
struct World {
    /// The stand-in for `/home/alice`.
    home: TempDir,
    /// The stand-in for the account-owned staging directory.
    staging: TempDir,
    /// The stand-in for the account-owned parking directory.
    parked: TempDir,
}

impl World {
    /// A home holding a live site, a staging directory holding the restored
    /// copy of it, and an empty parking directory.
    fn new() -> Self {
        let world = Self::without_a_live_site();
        create_dir_all(world.home.path().join(SITE)).unwrap();
        write(
            world.home.path().join(SITE).join("old.txt"),
            "the live site",
        )
        .unwrap();

        world
    }

    /// The same, but the customer has already deleted the site's directory.
    fn without_a_live_site() -> Self {
        let world = Self {
            home: TempDir::new().unwrap(),
            staging: TempDir::new().unwrap(),
            parked: TempDir::new().unwrap(),
        };
        create_dir_all(world.home.path().join("sites")).unwrap();
        create_dir_all(world.staging.path().join("shop.example.com")).unwrap();
        write(
            world
                .staging
                .path()
                .join("shop.example.com")
                .join("new.txt"),
            "the restored site",
        )
        .unwrap();

        world
    }

    /// The swap this world describes.
    fn swap(&self) -> DirectorySwap {
        DirectorySwap {
            home: self.home.path().to_path_buf(),
            staging: self.staging.path().to_path_buf(),
            parked: self.parked.path().to_path_buf(),
            directory: RelativePath::parse(SITE).unwrap(),
        }
    }
}

/// The inverse control without which every refusal below would pass just as well
/// against a swap that refused everything: a sound swap puts the restored tree in
/// place and parks the old one whole.
#[test]
fn a_sound_swap_installs_the_restored_tree_and_parks_the_old_one() {
    let world = World::new();

    swap_directory_as_account(&world.swap(), current_uid().unwrap()).unwrap();

    let live = world.home.path().join(SITE);
    assert_eq!(
        read_to_string(live.join("new.txt")).unwrap(),
        "the restored site"
    );
    assert!(!live.join("old.txt").exists());
    assert_eq!(
        read_to_string(world.parked.path().join(PARKED_ENTRY).join("old.txt")).unwrap(),
        "the live site"
    );
    assert!(!world.staging.path().join("shop.example.com").exists());
}

/// A site whose directory the customer already deleted is exactly what a restore
/// is for: there is nothing to park, and the restored tree still lands.
#[test]
fn a_missing_live_directory_is_restored_without_anything_to_park() {
    let world = World::without_a_live_site();

    swap_directory_as_account(&world.swap(), current_uid().unwrap()).unwrap();

    assert_eq!(
        read_to_string(world.home.path().join(SITE).join("new.txt")).unwrap(),
        "the restored site"
    );
    assert!(!world.parked.path().join(PARKED_ENTRY).exists());
}

/// An extraction that left nothing to install is refused BEFORE the live directory
/// is moved aside: moving it and then finding nothing to put in its place would
/// turn a refusal into an outage.
#[test]
fn nothing_to_install_leaves_the_live_directory_where_it_is() {
    let world = World::new();
    std::fs::remove_dir_all(world.staging.path().join("shop.example.com")).unwrap();

    let error = swap_directory_as_account(&world.swap(), current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryNotInBackup);
    assert_eq!(
        read_to_string(world.home.path().join(SITE).join("old.txt")).unwrap(),
        "the live site"
    );
    assert!(!world.parked.path().join(PARKED_ENTRY).exists());
}

/// A link standing where a directory on the way to the site should be is refused
/// and not followed: the walk never leaves the home.
#[test]
fn a_symbolic_link_on_the_way_to_the_site_is_refused_and_not_followed() {
    let world = World::without_a_live_site();
    let elsewhere = TempDir::new().unwrap();
    std::fs::remove_dir(world.home.path().join("sites")).unwrap();
    symlink(elsewhere.path(), world.home.path().join("sites")).unwrap();

    let error = swap_directory_as_account(&world.swap(), current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
    assert!(
        std::fs::read_dir(elsewhere.path())
            .unwrap()
            .next()
            .is_none(),
        "nothing was written through the link"
    );
}

/// A directory the account does not own is refused, whatever its permission bits
/// would allow: ownership is the claim that survives an account handing write
/// access around inside its own home.
#[test]
fn a_directory_owned_by_somebody_else_is_refused() {
    let world = World::new();
    let someone_else = current_uid().unwrap() + 1;

    let error = swap_directory_as_account(&world.swap(), someone_else).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
    assert_eq!(
        read_to_string(world.home.path().join(SITE).join("old.txt")).unwrap(),
        "the live site"
    );
}

/// A parent that does not exist is refused and not created: a single-directory
/// restore replaces a directory that is meant to be there.
#[test]
fn a_missing_parent_is_refused_and_not_created() {
    let world = World::new();
    std::fs::remove_dir_all(world.home.path().join("sites")).unwrap();

    let error = swap_directory_as_account(&world.swap(), current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
    assert!(!world.home.path().join("sites").exists());
}
