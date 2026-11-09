//! Opening the archive an extraction was asked to read.

use std::ffi::OsStr;
use std::io;
use std::os::unix::fs::FileExt;
use std::path::Path;

use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::archive_refusal::ArchiveRefusal;
use super::opened_archive::OpenedArchive;
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::open_directory_in_home::open_directory_in_home;

/// The first bytes of a zip's first local header.
const ZIP_MAGIC: [u8; 4] = [b'P', b'K', 3, 4];

/// The first bytes of a gzip stream.
const GZIP_MAGIC: [u8; 2] = [0x1f, 0x8b];

/// Opens `path` inside `home` as the account owning `uid`, and checks it is the format stated.
///
/// Runs inside the forked child. The parent directory is reached by the shared
/// descriptor walk (no link at any level, each level the account's), and the file
/// itself is opened with `O_NOFOLLOW`, so a symlink at the archive's name is
/// refused rather than followed. `O_NONBLOCK` keeps a FIFO planted at the name
/// from blocking the open forever; it is then refused as not a regular file.
/// The size comes from the descriptor, never from a second lookup.
///
/// The magic bytes are checked against the stated format, so a zip presented as
/// `tar.gz` (or the other way round) is refused here instead of being fed to the
/// wrong parser. The check is of the first bytes only; whether the rest is a
/// well-formed archive is the walk's question.
///
/// # Errors
///
/// [`ArchiveRefusal::HomeUnusable`] and [`ArchiveRefusal::DirectoryUnusable`]
/// from the walk, [`ArchiveRefusal::NotFound`] when the name is absent,
/// [`ArchiveRefusal::NotARegularFile`] for a link, a directory or anything else
/// that is not a regular file, and [`ArchiveRefusal::Unreadable`] when the first
/// bytes are not the stated format's.
pub(crate) fn open_archive(
    home: &Path,
    path: &BrowsePath,
    format: ArchiveFormat,
    uid: u32,
) -> Result<OpenedArchive, ArchiveRefusal> {
    let parent = path.parent().ok_or(ArchiveRefusal::NotARegularFile)?;
    let directory = open_directory_in_home(home, &parent, uid)
        .map_err(|error| ArchiveRefusal::from_walk_error(&error))?;

    let file = open_in_directory(
        &directory,
        OsStr::new(path.file_name()),
        libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC,
    )
    .map_err(|error| {
        if error.kind() == io::ErrorKind::NotFound {
            ArchiveRefusal::NotFound
        } else {
            ArchiveRefusal::NotARegularFile
        }
    })?;

    let metadata = file
        .metadata()
        .map_err(|_| ArchiveRefusal::NotARegularFile)?;
    if !metadata.is_file() {
        return Err(ArchiveRefusal::NotARegularFile);
    }

    let magic: &[u8] = match format {
        ArchiveFormat::Zip => &ZIP_MAGIC,
        ArchiveFormat::TarGz => &GZIP_MAGIC,
    };
    let mut first = [0_u8; 4];
    let first = &mut first[..magic.len()];
    file.read_exact_at(first, 0)
        .map_err(|_| ArchiveRefusal::Unreadable)?;
    if first != magic {
        return Err(ArchiveRefusal::Unreadable);
    }

    Ok(OpenedArchive {
        file,
        length: metadata.len(),
    })
}

#[cfg(test)]
#[path = "../../tests/files/archive/open_archive_tests.rs"]
mod tests;
