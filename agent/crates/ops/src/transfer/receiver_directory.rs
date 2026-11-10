//! A receiver directory that is removed whatever happens next.

use std::path::PathBuf;

use maran_agent_core::validation::system::name::AccountName;

use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_work_host::TransferWorkHost;

/// The directory an `rsync` run runs as an account reads its key from.
///
/// The twin of [`crate::transfer::TransferScratch`] for the one credential that
/// has to be readable by a customer's uid (decision D18, option a). Removal is
/// `Drop`, so it happens on a return, an early `?`, a panic unwind and a
/// cancelled future alike; no path has to remember it. A removal that fails is
/// logged with the directory's name (random, secret-free) and nothing of what it
/// held.
pub struct ReceiverDirectory<'a> {
    /// The host that created the directory and removes it.
    work: &'a dyn TransferWorkHost,
    /// The directory.
    path: PathBuf,
}

impl<'a> ReceiverDirectory<'a> {
    /// Creates a fresh receiver directory for `account` on `work`.
    ///
    /// # Errors
    ///
    /// Returns the host's [`TransferOpError::ScratchUnavailable`].
    pub fn create(
        work: &'a dyn TransferWorkHost,
        account: &AccountName,
    ) -> Result<Self, TransferOpError> {
        let path = work.create_receiver_directory(account)?;

        Ok(Self { work, path })
    }

    /// The path of the file called `name` in the directory.
    #[must_use]
    pub fn file(&self, name: &str) -> PathBuf {
        self.path.join(name)
    }
}

impl Drop for ReceiverDirectory<'_> {
    /// Removes the directory and everything in it.
    fn drop(&mut self) {
        if self.work.remove_receiver_directory(&self.path).is_err() {
            tracing::warn!(
                target: "maran_transfer",
                receiver = %self.path.display(),
                "a transfer receiver directory could not be removed"
            );
        }
    }
}
