//! Restores a configuration file if the operation that replaced it does not finish.

use std::fs;
use std::path::PathBuf;

/// Holds what was there before, and puts it back unless [`RollbackGuard::commit`]
/// is called.
///
/// A guard rather than an `if` at each error path: there are five ways out of
/// the write sequence after the rename, and the one that gets forgotten is the
/// one that leaves a server unable to start. [`super::render_validate_swap::write_config`]
/// still calls [`RollbackGuard::restore`] explicitly on every known failure
/// path, because only that call site can turn a failed restoration into a
/// typed [`super::SafeWriteError::RollbackFailed`] and return it to the
/// caller; `Drop` remains underneath as the guard against a path that forgets
/// to, restoring on a best-effort basis when the explicit call never happens.
pub struct RollbackGuard {
    target: PathBuf,
    previous: Option<Vec<u8>>,
    armed: bool,
}

impl RollbackGuard {
    /// Captures what `target` held before the write sequence — its bytes, or
    /// the fact that it did not exist — so it can be put back later.
    #[must_use]
    pub fn new(target: PathBuf, previous: Option<Vec<u8>>) -> Self {
        Self {
            target,
            previous,
            armed: true,
        }
    }

    /// Restores the captured content: writes the previous bytes back, or —
    /// when the target did not exist before — removes it.
    ///
    /// Disarms the guard either way, so `Drop` does not attempt the same
    /// restoration a second time.
    ///
    /// # Errors
    ///
    /// Returns the underlying I/O error when writing or removing the target
    /// fails. The caller is the one that knows how to fold this into a typed
    /// [`super::SafeWriteError::RollbackFailed`] alongside the failure that
    /// made rollback necessary.
    pub fn restore(&mut self) -> std::io::Result<()> {
        self.armed = false;
        match &self.previous {
            Some(bytes) => fs::write(&self.target, bytes),
            None => match fs::remove_file(&self.target) {
                Ok(()) => Ok(()),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
                Err(error) => Err(error),
            },
        }
    }

    /// Declares the write successful: the guard will not touch `target` again.
    pub fn commit(mut self) {
        self.armed = false;
    }
}

impl Drop for RollbackGuard {
    /// Best-effort safety net for a code path that returns without calling
    /// [`Self::restore`] or [`Self::commit`] explicitly.
    ///
    /// `Drop` cannot report failure, so an error here is not surfaced as a
    /// typed [`super::SafeWriteError`] the way [`Self::restore`] is — this is
    /// deliberately the second line of defence, not the primary one.
    fn drop(&mut self) {
        if self.armed {
            let _ = self.restore();
        }
    }
}
