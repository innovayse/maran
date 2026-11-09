//! The name a traffic cursor gives a log file.

use sha2::{Digest, Sha256};

use crate::sites::SitesOpError;
use crate::sites::site_log_file::SiteLogFile;
use crate::sites::traffic_budget::FINGERPRINT_BYTES;

/// The identity of `file` as a cursor positioned at `offset` records it:
/// `device:inode:fingerprint`.
///
/// The fingerprint is 64 bits of a SHA-256 over the file's first
/// `min(FINGERPRINT_BYTES, offset)` bytes. Device and inode alone are not an
/// identity, because a filesystem may hand a deleted file's inode to the next
/// file created; the leading bytes of an access log begin with a visitor
/// address and a timestamp, so a different file at a recycled inode differs
/// here. Covering only the bytes below the cursor is what keeps the name stable
/// while the file grows: a fingerprint of bytes that were not there yet when the
/// cursor was made could never match again.
///
/// A hash and not the bytes, so the cursor the panel stores carries no part of
/// a visitor's address.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the read fails.
pub(crate) fn log_file_identity(
    file: &dyn SiteLogFile,
    offset: u64,
) -> Result<String, SitesOpError> {
    let wanted = usize::try_from(offset.min(FINGERPRINT_BYTES)).unwrap_or(0);
    let mut head = vec![0_u8; wanted];
    let mut filled = 0;

    while filled < wanted {
        let read = file.read_at(filled as u64, &mut head[filled..])?;
        if read == 0 {
            break;
        }
        filled += read;
    }

    let digest = Sha256::digest(&head[..filled]);
    let fingerprint = digest
        .iter()
        .take(8)
        .fold(0_u64, |acc, byte| (acc << 8) | u64::from(*byte));

    Ok(format!(
        "{}:{}:{fingerprint:016x}",
        file.device(),
        file.inode()
    ))
}

#[cfg(test)]
#[path = "../tests/sites/log_file_identity_tests.rs"]
mod tests;
