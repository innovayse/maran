//! Copying an entry, and everything below it, into a directory that is being staged.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io::{self, Read};
use std::os::unix::fs::{MetadataExt, PermissionsExt};

use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;
use crate::files::entry_open_flags::ENTRY_OPEN_FLAGS;
use crate::files::io_failure::io_failure;
use crate::files::model::limit_kind::LimitKind;
use crate::files::open_home::DIRECTORY_FLAGS;
use crate::files::tree::open_tree_directory::open_tree_directory;
use crate::files::tree::tree_limits::TreeLimits;
use crate::files::tree::tree_tally::TreeTally;

/// Permission bits a copied directory is created with, until its children are in.
///
/// Private, so a half-copied tree is never readable by anybody but the account,
/// and widened to the source's own bits (masked to `0o777`) on the descriptor
/// once the directory is complete. Doing it last also lets a read-only source
/// directory (`0o555`) be filled in first.
const STAGING_DIRECTORY_MODE: libc::mode_t = 0o700;

/// Permission bits a copied file is created with, before its content is in.
const STAGING_FILE_MODE: libc::mode_t = 0o600;

/// A copy of one tree in progress: who it runs as, what it may hold, and what it has made.
///
/// A struct and not a function of nine arguments because the figures that travel
/// with every step of the recursion (the account, the filesystem the walk began
/// on, the allowance and the running count) are one thing seen from four sides.
#[derive(Debug)]
pub(crate) struct CopyTree {
    /// The account's uid, which every directory entered must be owned by.
    uid: u32,
    /// The filesystem the copy began on; a directory on another is refused.
    device: u64,
    /// The allowance the copy may not pass.
    limits: TreeLimits,
    /// What has been created so far.
    tally: TreeTally,
}

impl CopyTree {
    /// Starts a copy run as `uid` on `device` within `limits`.
    #[must_use]
    pub(crate) fn new(uid: u32, device: u64, limits: TreeLimits) -> Self {
        Self {
            uid,
            device,
            limits,
            tally: TreeTally::default(),
        }
    }

    /// The uid the copy runs as.
    #[must_use]
    pub(crate) fn uid(&self) -> u32 {
        self.uid
    }

    /// What the copy has created so far: entries and bytes.
    #[must_use]
    pub(crate) fn tally(&self) -> TreeTally {
        self.tally
    }

    /// Copies `name` inside `source_parent` to `target_name` inside `target_parent`.
    ///
    /// Meant to run after `scan_tree` has proved the tree holds only regular
    /// files and directories within the stated bounds, and it does not rely on
    /// that having stayed true: every entry is `fstatat`ed again before it is
    /// touched and anything but a regular file or a directory refuses the copy,
    /// a source file is opened `O_NOFOLLOW | O_NONBLOCK` and judged by `fstat`
    /// on the descriptor (so a FIFO planted after the scan cannot block the
    /// child and a link swapped in is refused), and a directory is entered
    /// through `open_tree_directory`. Content is read through the descriptor
    /// only, and the byte limit is enforced by the bytes actually copied and not
    /// by the scan's estimate, which a racing customer can outgrow.
    ///
    /// Owner and times are not preserved; permission bits are, masked to
    /// `0o777`, so a setuid bit on the source never reaches the copy.
    ///
    /// The target is expected to be a directory this operation is staging (see
    /// `copy_in_home`): on any error what was created here is left for the
    /// caller to remove, which is the only reason a failed copy leaves nothing
    /// visible. `depth` is the number of directories above this entry within
    /// the copy, zero for the root.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::NotFound`] when a source entry has vanished,
    /// [`FilesOpError::UnsupportedEntry`] for anything but a regular file or a
    /// directory, [`FilesOpError::LimitReached`] when the limits are passed,
    /// [`FilesOpError::TooDeep`] past the depth ceiling, the errors of
    /// `open_tree_directory`, [`FilesOpError::QuotaExceeded`] when the
    /// account's quota ends the copy, and [`FilesOpError::WriteFailed`] for
    /// every other failure to create, write or set the mode of a copy.
    pub(crate) fn copy(
        &mut self,
        source_parent: &File,
        name: &OsStr,
        target_parent: &File,
        target_name: &OsStr,
        depth: usize,
    ) -> Result<(), FilesOpError> {
        let stat = match stat_in_directory(source_parent, name) {
            Ok(stat) => stat,
            Err(error) if error.kind() == io::ErrorKind::NotFound => {
                return Err(FilesOpError::NotFound);
            }
            Err(_) => return Err(FilesOpError::DirectoryUnusable),
        };

        self.tally.entries = self.tally.entries.saturating_add(1);
        if self.tally.entries > self.limits.max_entries {
            return Err(FilesOpError::LimitReached(LimitKind::Entries));
        }

        match stat.kind {
            EntryKind::File => self.copy_file(source_parent, name, target_parent, target_name),
            EntryKind::Directory => {
                self.copy_directory(source_parent, name, target_parent, target_name, depth)
            }
            EntryKind::Symlink | EntryKind::Other => Err(FilesOpError::UnsupportedEntry),
        }
    }

    /// Copies one regular file through its descriptor.
    fn copy_file(
        &mut self,
        source_parent: &File,
        name: &OsStr,
        target_parent: &File,
        target_name: &OsStr,
    ) -> Result<(), FilesOpError> {
        let mut source = open_in_directory(source_parent, name, ENTRY_OPEN_FLAGS)
            .map_err(|_| FilesOpError::UnsupportedEntry)?;
        let metadata = source
            .metadata()
            .map_err(|_| FilesOpError::UnsupportedEntry)?;
        if !metadata.is_file() {
            return Err(FilesOpError::UnsupportedEntry);
        }

        let mut target = create_file_in_directory(target_parent, target_name, STAGING_FILE_MODE)
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;

        // One byte more than the room left, so exceeding it is observed and not
        // merely reached.
        let room = self.limits.max_bytes.saturating_sub(self.tally.bytes);
        let copied = io::copy(
            &mut source.by_ref().take(room.saturating_add(1)),
            &mut target,
        )
        .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;
        if copied > room {
            return Err(FilesOpError::LimitReached(LimitKind::Bytes));
        }
        self.tally.bytes = self.tally.bytes.saturating_add(copied);

        target
            .set_permissions(Permissions::from_mode(metadata.mode() & 0o777))
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))
    }

    /// Copies one directory and everything in it.
    fn copy_directory(
        &mut self,
        source_parent: &File,
        name: &OsStr,
        target_parent: &File,
        target_name: &OsStr,
        depth: usize,
    ) -> Result<(), FilesOpError> {
        if depth >= TreeLimits::MAXIMUM_DEPTH {
            return Err(FilesOpError::TooDeep);
        }

        let source = open_tree_directory(source_parent, name, self.uid, self.device)?;
        let source_mode = source
            .metadata()
            .map_err(|_| FilesOpError::DirectoryUnusable)?
            .mode()
            & 0o777;

        make_directory_in_directory(target_parent, target_name, STAGING_DIRECTORY_MODE)
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;
        let target = open_in_directory(target_parent, target_name, DIRECTORY_FLAGS)
            .map_err(|_| FilesOpError::DirectoryUnusable)?;

        let read = list_directory_names(
            &source,
            usize::try_from(self.limits.max_entries)
                .unwrap_or(usize::MAX)
                .saturating_add(1),
        )
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
        if read.exceeded {
            return Err(FilesOpError::LimitReached(LimitKind::Entries));
        }
        for child in &read.names {
            if child == OsStr::new(".") || child == OsStr::new("..") {
                continue;
            }
            self.copy(&source, child, &target, child, depth + 1)?;
        }

        target
            .set_permissions(Permissions::from_mode(source_mode))
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))
    }
}

#[cfg(test)]
#[path = "../../tests/files/tree/copy_tree_tests.rs"]
mod tests;
