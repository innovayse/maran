//! The ceiling one dump may reach, derived from the room the scratch
//! filesystem actually has right now.

use std::path::Path;

use maran_agent_core::utils::available_bytes::available_bytes;

use crate::backup::backup_error::BackupError;

/// The share of the scratch filesystem's free space a single dump may spend.
///
/// Nine tenths. The tenth that is left is not a safety margin for this
/// operation — it is room for everything else on the host to keep writing
/// while a backup runs, which is the failure the whole ceiling exists to
/// prevent: a host whose disk fills during a backup starts failing at
/// everything it does, not merely at the backup.
const SPENDABLE_NUMERATOR: u64 = 9;

/// The denominator of [`SPENDABLE_NUMERATOR`].
const SPENDABLE_DENOMINATOR: u64 = 10;

/// The smaller of `cap` and what the filesystem under `directory` can still
/// afford.
///
/// `directory` is the directory the dump file itself lands in — never a
/// configured string and never a parent, for the reason
/// [`available_bytes`]'s own doc gives. It must already exist; every caller
/// here measures after the scratch has been created.
///
/// This is deliberately re-measured for each dump rather than once per
/// operation. Nothing removes an individual dump: an operation over `N`
/// databases holds all `N` at once, so a ceiling computed once and applied `N`
/// times bounds `N · ceiling` and not `ceiling`. Measuring per dump makes each
/// write bounded by what is left after the previous ones.
///
/// It narrows a ceiling; it is not by itself a gate. `dump_database` still
/// enforces the number after the client has written, because on the creation
/// side nothing knows a dump's size in advance. The refusal that happens
/// BEFORE a write, where the sizes are known, is
/// [`crate::backup::require_scratch_room::require_scratch_room`].
///
/// # Errors
///
/// - [`BackupError::ScratchUnmeasurable`] when the filesystem cannot be asked.
/// - [`BackupError::ScratchTooSmall`] when nine tenths of what is left rounds
///   to nothing — a filesystem with no room is refused here rather than at
///   `ENOSPC` halfway through a customer's database.
pub(crate) fn scratch_dump_ceiling(directory: &Path, cap: u64) -> Result<u64, BackupError> {
    let available =
        available_bytes(directory).map_err(|_error| BackupError::ScratchUnmeasurable)?;

    narrow_to_room(available, cap)
}

/// The smaller of `cap` and the spendable share of `available`.
///
/// The arithmetic of [`scratch_dump_ceiling`], with the number a parameter
/// instead of a reading of the machine this runs on. It is a separate function
/// for the reason rules/testing.md gives for `resolve_under`'s injectable home
/// root: the exact figure is the part worth asserting, and it cannot be
/// asserted exactly against a filesystem whose free space moves between the
/// agent's reading and a test's own. A test that recomputed the expectation
/// from a second reading of the same live filesystem was measuring the host
/// rather than this code, and failed 7 times in 60 whole-suite runs on nothing
/// but that jitter — a few kilobytes of drift on a filesystem with 170 GiB
/// free. Stated here, the nine tenths are checked and nothing about the host
/// can move under the check.
///
/// What is left outside it — that the number handed in is a live reading of
/// the directory the dump lands in — is the one thing this split cannot
/// observe, and its caller's live tests are what hold that half up.
///
/// # Errors
///
/// - [`BackupError::ScratchTooSmall`] when the spendable share rounds to
///   nothing, which is any filesystem with fewer than
///   [`SPENDABLE_DENOMINATOR`] bytes left after integer division. Refused here
///   rather than at `ENOSPC` halfway through a customer's database.
fn narrow_to_room(available: u64, cap: u64) -> Result<u64, BackupError> {
    let spendable = available / SPENDABLE_DENOMINATOR * SPENDABLE_NUMERATOR;
    if spendable == 0 {
        return Err(BackupError::ScratchTooSmall {
            available,
            required: SPENDABLE_DENOMINATOR,
        });
    }

    Ok(cap.min(spendable))
}

#[cfg(test)]
#[path = "../tests/backup/scratch_dump_ceiling_tests.rs"]
mod tests;
