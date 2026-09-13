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
///
/// # Why the restore is conditional
///
/// The guard also holds `swapped_in` — the content the operation put at
/// `target`, or the fact that it took the file away. A restore only happens
/// when the target still holds exactly that. Restoring unconditionally is
/// unsound whenever the capture and the restore are not one critical section:
/// a certificate installation that captured a site's vhost, then failed after
/// a concurrent deletion had committed, would `fs::write` the deleted site's
/// vhost back — live, unowned, and removable by no rpc. That interleaving was
/// established by reading the code and is NOT driven anywhere: nothing on this
/// tree runs a certificate installation against a site deletion, which is why
/// the conditional below is the whole of the defence.
///
/// `config_tree_lock` makes that interleaving unreachable between two
/// operations of one agent, and this check is what holds where that lock
/// cannot reach: the lock is process-local, so an installer step, a second
/// agent binary or an operator's editor can still change the file between the
/// rename and the rollback. A rollback that discovers this refuses to act
/// rather than overwriting a change it knows nothing about, and says so in the
/// log — the failure it was rolling back is still returned to the caller
/// either way, so refusing here loses no error and invents no success.
///
/// # What it cannot hold
///
/// Every field below is in memory, and `Drop` runs on a return or an unwind —
/// never on a `SIGKILL`. So this guard protects against a path out of the
/// write protocol, and against nothing that ends the process. A kill between
/// the rename and the commit leaves the swapped-in content live with the guard
/// gone; see [`super::render_validate_swap::write_config`], "The midpoint that
/// argument does not cover", for what is exposed and what the next start does
/// and does not look at. Making the guard survive would mean writing its
/// capture to disk, which is a file of the agent's own on every host and so an
/// amendment to `rules/architecture.md`'s statelessness invariant, not a
/// change this type can make on its own.
pub struct RollbackGuard {
    target: PathBuf,
    previous: Option<Vec<u8>>,
    swapped_in: Option<Vec<u8>>,
    armed: bool,
}

impl RollbackGuard {
    /// Captures what `target` held before the write sequence — its bytes, or
    /// the fact that it did not exist — alongside `swapped_in`, the content
    /// the sequence is about to put there (or [`None`] when it takes the file
    /// away, which is what a removal does).
    ///
    /// Both halves are needed: `previous` is what a rollback would restore,
    /// and `swapped_in` is what the target must still hold for that rollback
    /// to be the guard's to make.
    #[must_use]
    pub fn new(target: PathBuf, previous: Option<Vec<u8>>, swapped_in: Option<Vec<u8>>) -> Self {
        Self {
            target,
            previous,
            swapped_in,
            armed: true,
        }
    }

    /// Restores the captured content: writes the previous bytes back, or —
    /// when the target did not exist before — removes it.
    ///
    /// Does nothing at all when the target no longer holds what this guard
    /// swapped in, because then the file is somebody else's and undoing this
    /// operation would undo theirs. That case is logged at `warn`: it means
    /// something outside this process wrote the agent's config tree, which an
    /// operator wants to know about.
    ///
    /// Disarms the guard either way, so `Drop` does not attempt the same
    /// restoration a second time.
    ///
    /// # Errors
    ///
    /// Returns the underlying I/O error when writing or removing the target
    /// fails, and when the target cannot be read to decide whether the
    /// restoration is this guard's to make — a target that cannot be read is
    /// not a target that may be overwritten blind. The caller is the one that
    /// knows how to fold this into a typed
    /// [`super::SafeWriteError::RollbackFailed`] alongside the failure that
    /// made rollback necessary.
    pub fn restore(&mut self) -> std::io::Result<()> {
        self.armed = false;

        if !self.target_still_holds_what_was_swapped_in()? {
            tracing::warn!(
                target_path = %self.target.display(),
                "a configuration file changed outside this operation; its rollback was refused"
            );
            return Ok(());
        }

        match &self.previous {
            Some(bytes) => fs::write(&self.target, bytes),
            None => match fs::remove_file(&self.target) {
                Ok(()) => Ok(()),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
                Err(error) => Err(error),
            },
        }
    }

    /// Whether `target` still holds the content this guard swapped in.
    ///
    /// A removal swapped in nothing, so for it the answer is whether the
    /// target is still absent.
    ///
    /// # Errors
    ///
    /// Returns the underlying I/O error when the target exists but cannot be
    /// read. Not existing is an answer, not an error.
    fn target_still_holds_what_was_swapped_in(&self) -> std::io::Result<bool> {
        let current = match fs::read(&self.target) {
            Ok(bytes) => Some(bytes),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => None,
            Err(error) => return Err(error),
        };

        Ok(current == self.swapped_in)
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

#[cfg(test)]
#[path = "../tests/safe_write/rollback_guard_tests.rs"]
mod tests;
