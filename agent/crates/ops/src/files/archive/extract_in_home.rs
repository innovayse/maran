//! Extracting an archive into a customer's home, as the customer.

use std::io::Read;
use std::path::Path;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;
use super::data_access::DataAccess;
use super::expansion_budget::ExpansionBudget;
use super::extraction_writer::ExtractionWriter;
use super::open_archive::open_archive;
use super::refuse_reserved_location::refuse_reserved_location;
use super::refuse_reserved_member::refuse_reserved_member;
use super::walk_members::walk_members;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::extract_archive_input::ExtractArchiveInput;
use crate::files::open_home::open_home;

/// Extracts `input.archive` into `input.destination` below `home`, as the account owning `uid`.
///
/// Runs inside the forked child. In order, and what each step refuses:
///
/// 0. **Refuse the trash** as the archive or the destination: the service layer
///    already does, and this is the second layer, so the operation holds the
///    line whoever calls it ([`refuse_reserved_location`]).
/// 1. **Open the archive by descriptor** and check its magic against the stated
///    format ([`open_archive`]): no link followed, only a regular file.
/// 2. **Judge the whole archive before writing a byte.** The first walk reads
///    every member (a zip's central directory, a tar.gz's headers through a
///    bounded stream) and refuses the archive as a whole for any link, device,
///    FIFO, sparse member, unsafe name, collision between members, unsupported
///    compression, or declared size, entry count or ratio over the request's
///    caps, and for any member that would land in the account's trash.
/// 3. **Write, judging again.** The second walk repeats every check, because the
///    archive is the customer's file and may differ from what the first walk
///    saw; each member is then written by [`ExtractionWriter`] through
///    descriptors, with no overwrite, counting the bytes actually produced.
/// 4. **On any failure, remove what this run created** and nothing else.
///
/// Idempotency differs from most operations here and is stated plainly: running
/// it a second time is NOT a no-op. The first run created the files, so the
/// second is refused with `AlreadyExists` at the first of them and changes
/// nothing.
///
/// # Errors
///
/// Every [`ArchiveRefusal`] the steps above name.
pub(crate) fn extract_in_home(
    home: &Path,
    input: &ExtractArchiveInput,
    uid: u32,
) -> Result<ArchiveReport, ArchiveRefusal> {
    refuse_reserved_location(&input.archive)?;
    refuse_reserved_location(&input.destination)?;

    let opened = open_archive(home, &input.archive, input.format, uid)?;

    let mut scan_budget = ExpansionBudget::for_extraction(&input.bounds, opened.length);
    walk_members(
        &opened.file,
        opened.length,
        input.format,
        DataAccess::Skip,
        &mut scan_budget,
        &mut |member: &ArchiveMember, _data: &mut dyn Read| {
            refuse_reserved_member(input.destination.is_home(), member)
        },
    )?;

    let home_directory =
        open_home(home, uid).map_err(|error| ArchiveRefusal::from_walk_error(&error))?;
    let mut writer = ExtractionWriter::new(
        home_directory,
        uid,
        input.destination.components(),
        &input.bounds,
        opened.length,
    )?;

    let mut write_pass_budget = ExpansionBudget::for_extraction(&input.bounds, opened.length);
    let outcome = writer.prepare().and_then(|()| {
        walk_members(
            &opened.file,
            opened.length,
            input.format,
            DataAccess::Read,
            &mut write_pass_budget,
            &mut |member: &ArchiveMember, data: &mut dyn Read| writer.write(member, data),
        )
    });

    match outcome {
        Ok(()) => Ok(writer.report()),
        Err(refusal) => {
            writer.roll_back();
            Err(refusal)
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/extract_in_home_tests.rs"]
mod tests;
