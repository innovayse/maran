//! Walking the members of a gzip-compressed tar archive.

use std::fs::File;
use std::io::{self, BufReader, Read, Seek, SeekFrom};

use flate2::read::GzDecoder;
use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;
use tar::{Archive, EntryType};

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;
use super::counting_reader::CountingReader;
use super::data_access::DataAccess;
use super::expansion_budget::ExpansionBudget;
use super::member_kind::MemberKind;
use super::member_registry::MemberRegistry;
use super::member_visitor::MemberVisitor;

/// Hands every member of the `.tar.gz` `file` to `visit`, after judging it.
///
/// The archive is streamed through a gzip decoder into the tar reader with
/// fixed buffers: nothing is materialised, so the memory this costs does not
/// depend on the archive. Each header is judged before its data is touched:
/// entry type (a link, device, FIFO or sparse file refuses the whole archive),
/// name (raw bytes, through [`ArchiveMemberName`], never the crate's `path()`),
/// the declared size against `budget`, and the registry. A tar's declared size
/// is the framing of the stream itself, so unlike a zip a header cannot claim
/// less than the entry holds; the bytes are still counted again by whoever
/// writes them.
///
/// The decompressed stream is bounded independently of what any header says
/// ([`CountingReader`], by [`ExpansionBudget::tar_stream_limit`]), so padding, an endless run of zero blocks or a gzip
/// member that never ends cannot make the walk run on. After the last entry the
/// remainder of the stream is drained to its end, which makes the gzip trailer
/// (length and CRC) be checked, and any non-zero byte there refuses the archive:
/// bytes after the end marker are bytes no tar reader would show, which is
/// where a second archive hides.
///
/// A leading `./` is accepted on every name, because `tar` writes it whenever it
/// is told to archive `.`, and a member that is just `.` or `./` is the
/// destination itself and is skipped when it is a directory. Nothing else
/// containing `.` as a component is accepted. A pax GLOBAL header carries
/// metadata for no file and is skipped (`git archive` writes one); the extension
/// headers that describe a single entry are consumed by the crate itself.
///
/// # Errors
///
/// [`ArchiveRefusal::Unreadable`] when the stream is not a gzip-compressed tar,
/// [`ArchiveRefusal::ByteLimit`] when the decompressed stream outgrows its
/// bound, the budget's refusals, [`ArchiveRefusal::TrailingData`], whatever the
/// registry refuses, and whatever `visit` returns.
pub(crate) fn walk_tar_gz_members(
    file: &File,
    access: DataAccess,
    budget: &mut ExpansionBudget,
    visit: &mut MemberVisitor<'_>,
) -> Result<(), ArchiveRefusal> {
    let mut reader = BufReader::new(file);
    reader
        .seek(SeekFrom::Start(0))
        .map_err(|_| ArchiveRefusal::ReadFailed)?;
    let stream = CountingReader::new(GzDecoder::new(reader), budget.tar_stream_limit());
    let mut archive = Archive::new(stream);
    let mut registry = MemberRegistry::new();

    {
        let entries = archive.entries().map_err(stream_refusal)?;
        for entry in entries {
            let mut entry = entry.map_err(stream_refusal)?;

            let kind = match entry.header().entry_type() {
                EntryType::XGlobalHeader => continue,
                EntryType::Regular | EntryType::Continuous => MemberKind::File,
                EntryType::Directory => MemberKind::Directory,
                EntryType::Symlink | EntryType::Link => return Err(ArchiveRefusal::LinkMember),
                EntryType::GNUSparse => return Err(ArchiveRefusal::SparseMember),
                _ => return Err(ArchiveRefusal::SpecialMember),
            };

            let raw = entry.path_bytes().into_owned();
            let trimmed = raw.strip_prefix(b"./").unwrap_or(&raw);
            if (raw == b"." || trimmed.is_empty()) && kind == MemberKind::Directory {
                continue;
            }
            let name = ArchiveMemberName::parse(trimmed).map_err(|_| ArchiveRefusal::UnsafeName)?;
            if kind == MemberKind::File && name.had_trailing_separator() {
                return Err(ArchiveRefusal::UnsafeName);
            }

            let declared_size = match kind {
                MemberKind::File => entry.size(),
                MemberKind::Directory => 0,
            };
            let mode = entry.header().mode().unwrap_or(0);
            let member = ArchiveMember {
                name,
                kind,
                executable: kind == MemberKind::File && mode & 0o111 != 0,
                declared_size,
            };

            budget.admit_entry()?;
            budget.admit_bytes(member.declared_size)?;
            registry.admit(&member)?;

            match access {
                DataAccess::Skip => visit(&member, &mut io::empty())?,
                DataAccess::Read => visit(&member, &mut entry)?,
            }
        }
    }

    drain_to_end(archive.into_inner())
}

/// Reads the rest of the decompressed stream, requiring it to be zeros.
///
/// # Errors
///
/// [`ArchiveRefusal::TrailingData`] on a non-zero byte, [`ArchiveRefusal::ByteLimit`]
/// when the stream outgrows its bound, [`ArchiveRefusal::Unreadable`] when gzip
/// reports damage (its trailer included).
fn drain_to_end<R: Read>(mut stream: R) -> Result<(), ArchiveRefusal> {
    let mut buffer = [0_u8; 16 * 1024];
    loop {
        let read = match stream.read(&mut buffer) {
            Ok(0) => return Ok(()),
            Ok(read) => read,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(stream_refusal(error)),
        };
        if buffer[..read].iter().any(|byte| *byte != 0) {
            return Err(ArchiveRefusal::TrailingData);
        }
    }
}

/// Reads a failure of the decompressed stream.
///
/// The bound being hit is the one failure with a meaning of its own; every
/// other error is the gzip or tar layer saying the bytes are not what was
/// stated.
fn stream_refusal(error: io::Error) -> ArchiveRefusal {
    if error.kind() == io::ErrorKind::FileTooLarge {
        ArchiveRefusal::ByteLimit
    } else {
        ArchiveRefusal::Unreadable
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/walk_tar_gz_members_tests.rs"]
mod tests;
