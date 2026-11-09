//! PurgeExpiredTrash: the retention applied to the account's trash.

use crate::files::model::purge_expired_trash_input::PurgeExpiredTrashInput;
use crate::files::{FilesHost, FilesOpError};

/// Removes the trash entries older than the retention, and sweeps the copy
/// staging of leftovers older than a day, and answers how many trash entries went.
///
/// The agent keeps no schedule: the panel calls this once a day per account. The
/// retention is non-zero by type, because "older than zero days" can only mean
/// everything, which is [`super::empty_trash`] and must be asked for by name. Age
/// is read from the entry's identifier and compared strictly. Idempotent.
///
/// # Errors
///
/// Returns [`FilesOpError::Saturated`] at the bulk bound, and
/// [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`], [`FilesOpError::TooDeep`],
/// [`FilesOpError::LimitReached`] and [`FilesOpError::RemoveFailed`].
pub fn purge_expired_trash(
    host: &dyn FilesHost,
    input: &PurgeExpiredTrashInput,
) -> Result<u64, FilesOpError> {
    host.purge_expired_trash_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/purge_expired_trash_tests.rs"]
mod tests;
