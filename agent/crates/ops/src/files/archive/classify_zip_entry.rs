//! Judging one zip entry from its central directory record alone.

use std::io::Read;

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;
use zip::CompressionMethod;
use zip::read::ZipFile;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;
use super::member_kind::MemberKind;

/// Mask of the file-type bits of a unix mode.
const TYPE_MASK: u32 = 0o170_000;

/// Unix file type: directory.
const TYPE_DIRECTORY: u32 = 0o040_000;

/// Unix file type: regular file.
const TYPE_REGULAR: u32 = 0o100_000;

/// Unix file type: symbolic link.
const TYPE_SYMLINK: u32 = 0o120_000;

/// Turns the metadata of a zip entry into a member, or refuses the archive.
///
/// Reads the name as RAW bytes and parses it with [`ArchiveMemberName`]: the zip
/// crate's own `name()`, `enclosed_name()` and `mangled_name()` are never asked,
/// because each of them answers "what path is this?" in the crate's way and the
/// extraction must write only what the agent's own parser accepted.
///
/// The kind comes from the unix mode when the entry has one, and a mode that is
/// not a regular file or a directory is a refusal, never a skipped entry: an
/// archive that carries a link is refused as a whole. An entry written on a
/// system without unix modes is a file, or a directory when its name ends in a
/// separator. Encrypted entries and compression methods other than stored and
/// deflate are refused by name, whatever the crate is built to decode.
///
/// # Errors
///
/// [`ArchiveRefusal::UnsafeName`], [`ArchiveRefusal::LinkMember`],
/// [`ArchiveRefusal::SpecialMember`], [`ArchiveRefusal::EncryptedMember`] and
/// [`ArchiveRefusal::UnsupportedCompression`].
pub(crate) fn classify_zip_entry<R: Read + ?Sized>(
    entry: &ZipFile<'_, R>,
) -> Result<ArchiveMember, ArchiveRefusal> {
    if entry.encrypted() {
        return Err(ArchiveRefusal::EncryptedMember);
    }
    if !matches!(
        entry.compression(),
        CompressionMethod::Stored | CompressionMethod::Deflated
    ) {
        return Err(ArchiveRefusal::UnsupportedCompression);
    }

    let name =
        ArchiveMemberName::parse(entry.name_raw()).map_err(|_| ArchiveRefusal::UnsafeName)?;

    let mode = entry.unix_mode();
    let named_as_directory = name.had_trailing_separator();
    let kind = match mode.map(|mode| mode & TYPE_MASK) {
        Some(TYPE_SYMLINK) => return Err(ArchiveRefusal::LinkMember),
        Some(TYPE_DIRECTORY) => MemberKind::Directory,
        Some(TYPE_REGULAR | 0) | None => {
            if named_as_directory {
                MemberKind::Directory
            } else {
                MemberKind::File
            }
        }
        Some(_) => return Err(ArchiveRefusal::SpecialMember),
    };

    let executable = mode.is_some_and(|mode| mode & 0o111 != 0);
    let declared_size = match kind {
        MemberKind::File => entry.size(),
        MemberKind::Directory => 0,
    };

    Ok(ArchiveMember {
        name,
        kind,
        executable: executable && kind == MemberKind::File,
        declared_size,
    })
}
