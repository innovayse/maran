//! The one place a file's SHA-256 is computed.

use std::fs::File;
use std::io::Read as _;
use std::path::Path;

use sha2::{Digest as _, Sha256};

use crate::backup::backup_error::BackupError;

/// How much of the file is read at a time.
///
/// Sixty-four kibibytes: large enough that a multi-gigabyte artifact is not
/// read a page at a time, small enough that hashing an artifact does not put a
/// meaningful buffer into a root daemon that may be doing several other things.
const CHUNK_BYTES: usize = 64 * 1024;

/// Hashes the file at `path` and answers its digest and its size.
///
/// SHA-256, hex, lowercase. Not a choice of taste: a restore decides whether to
/// load an archive into a live account by comparing this number, which makes it
/// a security-relevant digest, and rules/security.md item 9 rules out MD5 and
/// SHA1 for exactly that. It also rules out writing the function here — this
/// calls a vetted implementation.
///
/// Streamed rather than read whole. The artifact is a customer's entire home
/// and can be tens of gigabytes; `fs::read` on it is a root daemon killed by
/// the kernel, which is a backup that fails at the last step after doing all
/// the work.
///
/// The size comes back from the same read that produced the digest, rather than
/// from a separate `metadata` call. That is deliberate: the two would otherwise
/// be answers about the file at two different moments, and the pair
/// (digest, size) is what a restore checks an artifact against.
///
/// # Errors
///
/// Returns [`BackupError::ChecksumUnreadable`] when the file cannot be opened
/// or cannot be read to the end. A short read is a failure and never a shorter
/// file: a digest over fewer bytes than the file holds is a number that
/// describes nothing, and recording it would make a later verification pass on
/// an archive nobody has checked.
pub(crate) fn checksum_file(path: &Path) -> Result<(String, u64), BackupError> {
    let mut file = File::open(path).map_err(|_| BackupError::ChecksumUnreadable)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; CHUNK_BYTES];
    let mut total: u64 = 0;

    loop {
        let read = file
            .read(&mut buffer)
            .map_err(|_| BackupError::ChecksumUnreadable)?;
        if read == 0 {
            break;
        }

        hasher.update(&buffer[..read]);
        total = total
            .checked_add(read as u64)
            .ok_or(BackupError::ChecksumUnreadable)?;
    }

    Ok((hex(&hasher.finalize()), total))
}

/// The digest as lowercase hex.
///
/// Written out rather than pulled from a hex crate: it is four lines, and a
/// dependency added for four lines is a dependency the licence pass has to
/// carry forever.
fn hex(digest: &[u8]) -> String {
    let mut rendered = String::with_capacity(digest.len() * 2);
    for byte in digest {
        rendered.push_str(&format!("{byte:02x}"));
    }

    rendered
}

#[cfg(test)]
#[path = "../../tests/backup/archive/checksum_file_tests.rs"]
mod tests;
