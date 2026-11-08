//! Moving between directories of a customer's tree without ever naming a path twice.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};

use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use super::archive_refusal::ArchiveRefusal;
use super::extraction_ledger::ExtractionLedger;
use super::write_refusal::write_refusal;
use crate::files::open_home::DIRECTORY_FLAGS;

/// Mode a directory is created with, before it is widened on its own descriptor.
const CREATE_MODE: libc::mode_t = 0o700;

/// Mode a directory this cursor created ends up with.
///
/// The archive's mode for a directory is never used: one that arrived as
/// world-writable or setgid would be a decision the archive made about the
/// account's tree.
const CREATED_MODE: u32 = 0o755;

/// A chain of open directories below one root, kept between members.
///
/// Archives list members in directory order, so the next member almost always
/// shares its parent with the last. The cursor keeps the descriptors of the
/// chain it is in and walks only the part that changed, which is what makes
/// 100 000 members cost 100 000 `openat`s and not 100 000 times their depth.
/// Every step is `openat` with `O_NOFOLLOW | O_DIRECTORY` from the descriptor
/// above, and every directory must be the account's: nothing is resolved by
/// path, so a level swapped for a symlink mid-run is refused, not followed.
///
/// With a ledger the cursor CREATES a missing level (and records it, so a failed
/// run can take it away again); without one it only descends.
#[derive(Debug)]
pub(crate) struct DirectoryCursor {
    /// The uid every directory must belong to.
    uid: u32,
    /// The directory every path is relative to.
    root: File,
    /// The directories entered so far, outermost first, each with its own name.
    chain: Vec<(String, File)>,
}

impl DirectoryCursor {
    /// A cursor standing in `root`.
    pub(crate) fn new(root: File, uid: u32) -> Self {
        Self {
            uid,
            root,
            chain: Vec::new(),
        }
    }

    /// Moves to the directory `components` name below the root and returns it.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::AlreadyExists`] when, creating, a level's name is held by
    /// something that is not a directory (a link planted there included);
    /// [`ArchiveRefusal::NotFound`] when, not creating, a level is absent;
    /// [`ArchiveRefusal::DirectoryUnusable`] for a level that is not a directory
    /// the account owns, and the write refusals for a level that cannot be made.
    pub(crate) fn enter(
        &mut self,
        components: &[&str],
        mut ledger: Option<&mut ExtractionLedger>,
    ) -> Result<&File, ArchiveRefusal> {
        let keep = self
            .chain
            .iter()
            .zip(components)
            .take_while(|((name, _), wanted)| name.as_str() == **wanted)
            .count();
        self.chain.truncate(keep);

        for (index, component) in components.iter().enumerate().skip(keep) {
            let parent = self
                .chain
                .last()
                .map_or(&self.root, |(_, directory)| directory);
            let (child, created) =
                open_cursor_level(parent, component, self.uid, ledger.is_some())?;
            if created && let Some(ledger) = ledger.as_deref_mut() {
                ledger.record_directory(components[..=index].join("/"));
            }
            self.chain.push(((*component).to_owned(), child));
        }

        Ok(self
            .chain
            .last()
            .map_or(&self.root, |(_, directory)| directory))
    }
}

/// Opens one level below `parent`, making it first when `creating`.
///
/// Returns the directory and whether this call made it.
///
/// # Errors
///
/// As [`DirectoryCursor::enter`].
fn open_cursor_level(
    parent: &File,
    name: &str,
    uid: u32,
    creating: bool,
) -> Result<(File, bool), ArchiveRefusal> {
    let entry = OsStr::new(name);

    let mut created = false;
    if creating {
        match make_directory_in_directory(parent, entry, CREATE_MODE) {
            Ok(()) => created = true,
            // Already there is the ordinary case (a merge), and it is not trusted
            // to be a directory: the open below decides that.
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
            Err(error) => return Err(write_refusal(&error)),
        }
    }

    let directory = open_in_directory(parent, entry, DIRECTORY_FLAGS)
        .map_err(|error| refusal_of_open(&error, creating))?;

    let metadata = directory
        .metadata()
        .map_err(|_| ArchiveRefusal::DirectoryUnusable)?;
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(ArchiveRefusal::DirectoryUnusable);
    }

    if created {
        directory
            .set_permissions(Permissions::from_mode(CREATED_MODE))
            .map_err(|error| write_refusal(&error))?;
    }

    Ok((directory, created))
}

/// The refusal for a level that could not be opened as a directory.
///
/// A name held by a link or a file (`ELOOP`, `ENOTDIR`) is, to a run that must
/// create it, a name that is taken; to a run that only reads, a path that does
/// not lead where it was said to.
fn refusal_of_open(error: &io::Error, creating: bool) -> ArchiveRefusal {
    match error.raw_os_error() {
        Some(code) if code == libc::ELOOP || code == libc::ENOTDIR => {
            if creating {
                ArchiveRefusal::AlreadyExists
            } else {
                ArchiveRefusal::DirectoryUnusable
            }
        }
        Some(code) if code == libc::ENOENT && !creating => ArchiveRefusal::NotFound,
        _ => ArchiveRefusal::DirectoryUnusable,
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/directory_cursor_tests.rs"]
mod tests;
