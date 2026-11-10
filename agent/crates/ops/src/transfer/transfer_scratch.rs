//! A scratch directory that is removed whatever happens next.

use std::path::{Path, PathBuf};

use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// The file name of the private key inside a scratch directory.
pub(crate) const KEY_FILE_NAME: &str = "key";

/// The file name of the known_hosts file inside a scratch directory.
pub(crate) const KNOWN_HOSTS_FILE_NAME: &str = "known_hosts";

/// A root-only directory holding a credential for the length of one call.
///
/// Removal is `Drop`, so it happens on a return, an early `?`, a panic unwind
/// and a cancelled future alike; no path has to remember it. A removal that
/// fails is logged with the directory's name (random, secret-free) and nothing
/// of what it held.
pub struct TransferScratch<'a> {
    /// The host that created the directory and removes it.
    host: &'a dyn TransferHost,
    /// The directory.
    path: PathBuf,
}

impl<'a> TransferScratch<'a> {
    /// Creates a fresh scratch directory on `host`.
    ///
    /// # Errors
    ///
    /// Returns the host's [`TransferOpError::ScratchUnavailable`].
    pub fn create(host: &'a dyn TransferHost) -> Result<Self, TransferOpError> {
        let path = host.create_scratch()?;

        Ok(Self { host, path })
    }

    /// The directory.
    #[must_use]
    pub fn path(&self) -> &Path {
        &self.path
    }

    /// The path of the file called `name` in the directory.
    #[must_use]
    pub fn file(&self, name: &str) -> PathBuf {
        self.path.join(name)
    }
}

impl Drop for TransferScratch<'_> {
    /// Removes the directory and everything in it.
    fn drop(&mut self) {
        if self.host.remove_scratch(&self.path).is_err() {
            tracing::warn!(
                target: "maran_transfer",
                scratch = %self.path.display(),
                "a transfer scratch directory could not be removed"
            );
        }
    }
}

#[cfg(test)]
#[path = "../tests/transfer/transfer_scratch_tests.rs"]
mod tests;
