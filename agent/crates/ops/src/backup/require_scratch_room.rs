//! The pre-flight refusal: enough room for what is about to be written, asked
//! before anything is written.

use std::path::Path;

use maran_agent_core::utils::available_bytes::available_bytes;

use crate::backup::backup_error::BackupError;

/// Refuses unless the filesystem under `directory` can still take `required`
/// bytes.
///
/// `directory` is the directory the write lands in, never a parent — a parent
/// need not be the same mount, and on this product it often is not.
///
/// This is the gate the old per-dump ceiling was not. That number was compared
/// against a dump the client had already finished writing, so it could report
/// an overshoot but never refuse one; on a filesystem that runs out, the
/// process meets `ENOSPC` long before the comparison is reached. Here the size
/// is known in advance — the manifest records every dump's exact byte count —
/// so the operation can decline while everything is still untouched.
///
/// `required` is an estimate on the restore side and says so at its call site:
/// the rollback dumps it bounds are dumps of the LIVE databases, and the only
/// figures known before they are taken are the archive's. It is the right
/// order of magnitude and it is checked before a single database is dropped,
/// which is the property that matters.
///
/// # Errors
///
/// - [`BackupError::ScratchUnmeasurable`] when the filesystem cannot be asked.
///   Never a generous default: unknown is not plenty.
/// - [`BackupError::ScratchTooSmall`] when it has less than `required`.
pub(crate) fn require_scratch_room(directory: &Path, required: u64) -> Result<(), BackupError> {
    let available =
        available_bytes(directory).map_err(|_error| BackupError::ScratchUnmeasurable)?;

    if available < required {
        return Err(BackupError::ScratchTooSmall {
            available,
            required,
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/require_scratch_room_tests.rs"]
mod tests;
