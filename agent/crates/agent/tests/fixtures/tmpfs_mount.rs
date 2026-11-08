//! A tmpfs mounted over a directory for the life of one test.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use std::path::{Path, PathBuf};
use std::process::Command;

/// A tmpfs mounted over a directory for the life of one test, so a search
/// started above it meets a second device.
///
/// Mounting needs `CAP_SYS_ADMIN`, which only the privileged polygon lane has,
/// so [`TmpfsMount::mount`] answers `None` when the container refused and the
/// test then says `UNOBSERVED HERE` instead of passing silently.
pub struct TmpfsMount {
    /// The mount point.
    at: PathBuf,
}

impl TmpfsMount {
    /// Mounts a small tmpfs at `at`, which must exist. `None` when this
    /// container may not mount.
    pub fn mount(at: &Path) -> Option<Self> {
        let mounted = Command::new("mount")
            .args(["-t", "tmpfs", "-o", "size=1m", "tmpfs"])
            .arg(at)
            .status()
            .expect("the polygon image installs mount");

        mounted.success().then(|| Self {
            at: at.to_path_buf(),
        })
    }
}

impl Drop for TmpfsMount {
    /// Unmounts again, lazily, so a still-open descriptor cannot keep the mount
    /// and make the account's removal delete a live filesystem's contents.
    fn drop(&mut self) {
        let _ = Command::new("umount").arg("-l").arg(&self.at).status();
    }
}
