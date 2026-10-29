//! A configuration file that goes away when the test that wrote it ends.
//!
//! The polygon's nginx and php-fpm trees are shared by every test in a file, so a
//! file left behind by a failing test is parsed by the next test's `nginx -t` or
//! `php-fpm -t` — and its account has just been deleted, so the tree it poisons
//! fails for a reason that has nothing to do with what that next test was
//! checking. One failure then reads as several, which is precisely how a
//! genuinely untested protection hides inside a cascade.

use std::os::unix::fs::{FileTypeExt as _, OpenOptionsExt as _};
use std::path::{Path, PathBuf};

/// `O_NONBLOCK` on Linux, the same value on both polygon families and on x86_64
/// and aarch64.
const O_NONBLOCK: i32 = 0o4000;

/// Removes one configuration file when it is dropped, whether the test passed or
/// panicked.
///
/// Plain removal rather than `delete_site`: what is being cleaned up is this
/// file, and routing cleanup through an operation under test would make a
/// failure in that operation look like a failure everywhere.
pub struct PolygonConfigFile {
    /// The file to take away again.
    path: PathBuf,
}

impl PolygonConfigFile {
    /// Takes responsibility for the file at `path`.
    pub fn at(path: impl AsRef<Path>) -> Self {
        Self {
            path: path.as_ref().to_path_buf(),
        }
    }
}

impl Drop for PolygonConfigFile {
    /// Removes the file, reporting rather than panicking if it cannot.
    ///
    /// A FIFO is first opened for writing without blocking: a process parked in
    /// `open(2)` on it — the polygon's nginx master, which parses every `*.conf` on a
    /// reload — is released only by a writer, never by the unlink, and a master left
    /// parked serves its old configuration for the rest of the run, so every later
    /// test that reads what nginx serves would fail for this test's reason.
    fn drop(&mut self) {
        if std::fs::symlink_metadata(&self.path)
            .is_ok_and(|metadata| metadata.file_type().is_fifo())
        {
            let _ = std::fs::OpenOptions::new()
                .write(true)
                .custom_flags(O_NONBLOCK)
                .open(&self.path);
        }
        if let Err(error) = std::fs::remove_file(&self.path) {
            eprintln!(
                "the polygon file {:?} could not be removed: {error}",
                self.path
            );
        }
    }
}
