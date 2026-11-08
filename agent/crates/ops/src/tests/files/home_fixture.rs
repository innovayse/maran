//! A temporary home directory the file-manager mutation tests build trees in.
//!
//! Shared by the `*_in_home_tests.rs` and `tree/*_tests.rs` of the mutation
//! area through `#[path]` in `files/mod.rs`. The directory belongs to the test
//! process's own uid, which is what the descriptor walks check ownership against,
//! so no test here needs root.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::path::{Path, PathBuf};

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::mutable_path::MutablePath;

/// A temporary directory standing in for an account's home.
pub(crate) struct HomeFixture {
    /// The directory, removed when the fixture is dropped.
    directory: tempfile::TempDir,
}

impl HomeFixture {
    /// An empty home.
    pub(crate) fn new() -> Self {
        Self {
            directory: tempfile::tempdir().unwrap(),
        }
    }

    /// The home directory.
    pub(crate) fn home(&self) -> &Path {
        self.directory.path()
    }

    /// The uid the tests run as, which owns everything in the fixture.
    pub(crate) fn uid(&self) -> u32 {
        current_uid().unwrap()
    }

    /// The real path of `relative` inside the home.
    pub(crate) fn path(&self, relative: &str) -> PathBuf {
        self.directory.path().join(relative)
    }

    /// Creates a file with `contents`, and its parent directories.
    pub(crate) fn file(&self, relative: &str, contents: &str) {
        let path = self.path(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, contents).unwrap();
    }

    /// Creates a directory, and its parents.
    pub(crate) fn directory(&self, relative: &str) {
        std::fs::create_dir_all(self.path(relative)).unwrap();
    }

    /// Creates a symlink at `relative` pointing at `target`.
    pub(crate) fn link(&self, relative: &str, target: &Path) {
        let path = self.path(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::os::unix::fs::symlink(target, path).unwrap();
    }

    /// Creates a FIFO at `relative` with the system's `mkfifo`.
    pub(crate) fn fifo(&self, relative: &str) {
        let path = self.path(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        let made = std::process::Command::new("mkfifo")
            .arg(&path)
            .status()
            .unwrap();
        assert!(made.success(), "the test needs mkfifo to build its fixture");
    }

    /// The text of the file at `relative`.
    pub(crate) fn read(&self, relative: &str) -> String {
        std::fs::read_to_string(self.path(relative)).unwrap()
    }

    /// Whether anything, a dangling link included, is at `relative`.
    pub(crate) fn exists(&self, relative: &str) -> bool {
        std::fs::symlink_metadata(self.path(relative)).is_ok()
    }

    /// The permission bits of the entry at `relative`.
    pub(crate) fn mode(&self, relative: &str) -> u32 {
        std::fs::symlink_metadata(self.path(relative))
            .unwrap()
            .permissions()
            .mode()
            & 0o777
    }

    /// Sets the permission bits of the entry at `relative`.
    pub(crate) fn set_mode(&self, relative: &str, mode: u32) {
        std::fs::set_permissions(self.path(relative), std::fs::Permissions::from_mode(mode))
            .unwrap();
    }

    /// The device the entry at `relative` lives on.
    pub(crate) fn device(&self, relative: &str) -> u64 {
        std::fs::symlink_metadata(self.path(relative))
            .unwrap()
            .dev()
    }

    /// A directory descriptor for `relative`, as the walks hold them.
    pub(crate) fn open(&self, relative: &str) -> File {
        File::open(self.path(relative)).unwrap()
    }

    /// The names in the directory at `relative`, sorted.
    pub(crate) fn names(&self, relative: &str) -> Vec<String> {
        let mut names: Vec<String> = std::fs::read_dir(self.path(relative))
            .unwrap()
            .map(|entry| entry.unwrap().file_name().into_string().unwrap())
            .collect();
        names.sort();
        names
    }
}

/// Parses a mutable path the test knows is valid.
pub(crate) fn mutable(text: &str) -> MutablePath {
    MutablePath::parse(text).unwrap()
}
