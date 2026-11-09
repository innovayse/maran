//! Copying one member's bytes into the file being made, counting every one.

use std::io::{self, Read, Write};

use super::archive_refusal::ArchiveRefusal;
use super::expansion_budget::ExpansionBudget;
use super::write_refusal::write_refusal;

/// Bytes moved per step. A fixed buffer: nothing the archive says decides it.
const CHUNK: usize = 64 * 1024;

/// Copies `source` into `target`, charging `budget` for every byte as it is produced.
///
/// The count is of bytes actually PRODUCED by the decompressor, which is the only
/// figure an archive cannot lie about. The order of the checks per chunk is the
/// order of what the caller can act on: the budget first (the request's byte cap,
/// then the ratio), so a bomb is stopped by its cap; then the member's own
/// declared size, so a header that understates its entry is refused as a lie.
/// A member that ends before its declared size is a damaged archive.
///
/// # Errors
///
/// [`ArchiveRefusal::ByteLimit`] and [`ArchiveRefusal::BombRatio`] from the
/// budget, [`ArchiveRefusal::SizeMismatch`] when more arrives than was declared,
/// [`ArchiveRefusal::Unreadable`] when the source fails or ends short, and the
/// write refusals ([`ArchiveRefusal::Quota`], [`ArchiveRefusal::WriteFailed`]).
pub(crate) fn copy_member_data(
    source: &mut dyn Read,
    target: &mut dyn Write,
    budget: &mut ExpansionBudget,
    declared: u64,
) -> Result<u64, ArchiveRefusal> {
    let mut buffer = vec![0_u8; CHUNK];
    let mut copied = 0_u64;

    loop {
        let read = match source.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => read,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(_) => return Err(ArchiveRefusal::Unreadable),
        };
        let read_bytes = read as u64;

        budget.admit_bytes(read_bytes)?;
        copied += read_bytes;
        if copied > declared {
            return Err(ArchiveRefusal::SizeMismatch);
        }
        target
            .write_all(&buffer[..read])
            .map_err(|error| write_refusal(&error))?;
    }

    if copied != declared {
        return Err(ArchiveRefusal::Unreadable);
    }
    Ok(copied)
}

#[cfg(test)]
#[path = "../../tests/files/archive/copy_member_data_tests.rs"]
mod tests;
