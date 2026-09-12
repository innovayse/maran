//! Which state the disk is in, decided from inodes and never from a name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir_all, write};
use std::os::unix::fs::symlink;
use std::path::PathBuf;

use tempfile::TempDir;

use super::*;

/// The three paths of one swap, inside a directory the test owns.
struct Swap {
    /// The root, removed when the fixture drops.
    root: TempDir,
}

impl Swap {
    /// An empty tree: none of the three names exists yet.
    fn new() -> Self {
        Self {
            root: TempDir::new().expect("a temporary directory"),
        }
    }

    /// `/home/<account>`'s stand-in.
    fn home(&self) -> PathBuf {
        self.root.path().join("home")
    }

    /// The parked previous home's stand-in.
    fn previous(&self) -> PathBuf {
        self.root.path().join("previous")
    }

    /// The staging tree's stand-in.
    fn staging(&self) -> PathBuf {
        self.root.path().join("staging")
    }

    /// Creates the named directories.
    fn with_directories(self, names: &[&str]) -> Self {
        for name in names {
            create_dir_all(self.root.path().join(name)).expect("a directory of the fixture");
        }
        self
    }

    /// What `classify` says about this tree.
    fn state(&self) -> SwapState {
        SwapState::classify(&self.home(), &self.previous(), &self.staging())
    }
}

#[test]
fn a_marked_swap_that_has_not_moved_the_home_is_not_started() {
    let swap = Swap::new().with_directories(&["home", "staging"]);

    assert_eq!(swap.state(), SwapState::NotStarted);
}

#[test]
fn a_home_parked_beside_a_waiting_staging_tree_is_the_interrupted_window() {
    let swap = Swap::new().with_directories(&["previous", "staging"]);

    assert_eq!(swap.state(), SwapState::Interrupted);
}

#[test]
fn a_home_in_place_with_the_previous_tree_still_parked_is_swapped() {
    let swap = Swap::new().with_directories(&["home", "previous"]);

    assert_eq!(swap.state(), SwapState::Swapped);
}

#[test]
fn a_home_alone_is_a_completed_restore_and_not_something_to_recover() {
    let swap = Swap::new().with_directories(&["home"]);

    assert_eq!(swap.state(), SwapState::Completed);
}

#[test]
fn a_home_that_exists_beside_a_parked_one_is_refused_rather_than_decided() {
    let swap = Swap::new().with_directories(&["home", "previous", "staging"]);

    assert_eq!(swap.state(), SwapState::HomeAndParkedBoth);
}

#[test]
fn a_parked_home_with_no_staging_tree_left_is_the_rollback_case() {
    let swap = Swap::new().with_directories(&["previous"]);

    assert_eq!(swap.state(), SwapState::ParkedOnly);
}

#[test]
fn a_staging_tree_with_no_home_and_no_parked_copy_is_still_finishable() {
    let swap = Swap::new().with_directories(&["staging"]);

    assert_eq!(swap.state(), SwapState::StagingOnly);
}

#[test]
fn nothing_at_any_of_the_three_names_is_reported_as_the_loss_it_is() {
    let swap = Swap::new();

    assert_eq!(swap.state(), SwapState::NothingLeft);
}

#[test]
fn a_symbolic_link_standing_in_for_the_home_is_never_followed() {
    let swap = Swap::new().with_directories(&["elsewhere", "previous", "staging"]);
    symlink(swap.root.path().join("elsewhere"), swap.home()).expect("the link");

    // Without `symlink_metadata` this reads as `HomeAndParkedBoth` — the link
    // resolves to a directory — and the reconciliation would then remove a
    // staging tree on the strength of a directory somebody else chose.
    assert_eq!(swap.state(), SwapState::NotDirectories);
}

#[test]
fn a_regular_file_where_the_staging_tree_should_be_is_refused() {
    let swap = Swap::new().with_directories(&["previous"]);
    write(swap.staging(), b"not a home").expect("the file");

    assert_eq!(swap.state(), SwapState::NotDirectories);
}
