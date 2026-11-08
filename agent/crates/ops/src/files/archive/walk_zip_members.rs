//! Walking the members of a zip archive.

use std::fs::File;
use std::io::{self, BufReader, Seek, SeekFrom};

use zip::ZipArchive;

use super::archive_refusal::ArchiveRefusal;
use super::classify_zip_entry::classify_zip_entry;
use super::data_access::DataAccess;
use super::expansion_budget::ExpansionBudget;
use super::member_kind::MemberKind;
use super::member_registry::MemberRegistry;
use super::member_visitor::MemberVisitor;
use super::zip_central_entry_count::zip_central_entry_count;

/// Hands every member of the zip `file` to `visit`, after judging it.
///
/// The order of the checks is the order of what they cost. The declared entry
/// count is read from the end of the file first, so a central directory of
/// millions of entries is refused before the zip crate allocates for it. Each
/// entry is then judged from its central directory record alone (`by_index_raw`
/// does not decompress), charged against `budget` by its DECLARED size, and
/// recorded in the registry, all before `visit` runs. With [`DataAccess::Skip`]
/// that is all there is: nothing is decompressed, which is what makes the first
/// pass over a zip cheap. With [`DataAccess::Read`] the entry is then opened
/// for reading and its decompressed bytes go to `visit`; the crate checks the
/// CRC when the last byte is read, and a mismatch reaches `visit` as a read
/// error.
///
/// # Errors
///
/// [`ArchiveRefusal::Unreadable`] when the file is not a zip the crate can
/// parse, [`ArchiveRefusal::EntryLimit`], [`ArchiveRefusal::ByteLimit`] and
/// [`ArchiveRefusal::BombRatio`] from the budget, whatever
/// [`classify_zip_entry`] and the registry refuse, and whatever `visit` returns.
pub(crate) fn walk_zip_members(
    file: &File,
    length: u64,
    access: DataAccess,
    budget: &mut ExpansionBudget,
    visit: &mut MemberVisitor<'_>,
) -> Result<(), ArchiveRefusal> {
    if let Some(declared) = zip_central_entry_count(file, length) {
        budget.check_declared_entries(declared)?;
    }

    let mut reader = BufReader::new(file);
    reader
        .seek(SeekFrom::Start(0))
        .map_err(|_| ArchiveRefusal::ReadFailed)?;
    let mut archive = ZipArchive::new(reader).map_err(|_| ArchiveRefusal::Unreadable)?;
    let mut registry = MemberRegistry::new();

    for index in 0..archive.len() {
        let member = {
            let entry = archive
                .by_index_raw(index)
                .map_err(|_| ArchiveRefusal::Unreadable)?;
            classify_zip_entry(&entry)?
        };

        budget.admit_entry()?;
        budget.admit_bytes(member.declared_size)?;
        registry.admit(&member)?;

        match (access, member.kind) {
            (DataAccess::Skip, _) | (DataAccess::Read, MemberKind::Directory) => {
                visit(&member, &mut io::empty())?;
            }
            (DataAccess::Read, MemberKind::File) => {
                let mut entry = archive
                    .by_index(index)
                    .map_err(|_| ArchiveRefusal::Unreadable)?;
                visit(&member, &mut entry)?;
            }
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/files/archive/walk_zip_members_tests.rs"]
mod tests;
