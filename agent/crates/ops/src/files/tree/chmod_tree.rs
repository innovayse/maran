//! Changing the permission bits of an entry, and of everything below it.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::file_mode::FileMode;

use crate::files::FilesOpError;
use crate::files::entry_open_flags::ENTRY_OPEN_FLAGS;
use crate::files::model::permission_report::PermissionReport;
use crate::files::tree::open_tree_directory::open_tree_directory;
use crate::files::tree::tree_limits::TreeLimits;

/// A permission change in progress: the modes to apply, who it runs as, and what it reached.
///
/// A struct and not a function of eight arguments because the figures that
/// travel with every step of the recursion are one thing seen from several sides.
#[derive(Debug)]
pub(crate) struct ChmodTree {
    /// The bits for files, and for anything that is not a directory.
    file_mode: FileMode,
    /// The bits for directories.
    directory_mode: FileMode,
    /// The account's uid; entries of another owner are skipped.
    uid: u32,
    /// The filesystem the walk began on; a directory on another is skipped.
    device: u64,
    /// What has been changed and what has been left alone so far.
    report: PermissionReport,
}

impl ChmodTree {
    /// Starts a change as `uid` on `device`.
    #[must_use]
    pub(crate) fn new(
        file_mode: FileMode,
        directory_mode: FileMode,
        uid: u32,
        device: u64,
    ) -> Self {
        Self {
            file_mode,
            directory_mode,
            uid,
            device,
            report: PermissionReport::default(),
        }
    }

    /// What the change has reached and skipped so far.
    #[must_use]
    pub(crate) fn report(&self) -> PermissionReport {
        self.report
    }

    /// Counts one entry as left alone.
    fn skip(&mut self) {
        self.report.skipped = self.report.skipped.saturating_add(1);
    }

    /// Counts one entry as changed.
    fn change(&mut self) {
        self.report.changed = self.report.changed.saturating_add(1);
    }

    /// Sets the file mode on `name` inside `parent` and, for a directory, the
    /// directory mode on it and the file mode on everything below it.
    ///
    /// Every change is an `fchmod` on a descriptor opened without following
    /// links (so a link swapped in after the entry was judged is refused, and a
    /// link is never changed nor is its target), after an `fstat` of that
    /// descriptor proved it is the kind of thing and the owner expected. What
    /// cannot be changed is skipped and counted, never raised: a symlink, an
    /// entry the account does not own, a directory on another filesystem than
    /// the walk began on, and an entry that cannot be opened (a file the account
    /// has itself made unreadable cannot be opened to be changed back, and is
    /// reported rather than guessed at).
    ///
    /// A directory's children are changed before the directory, so a directory
    /// mode that removes the account's own right to enter it takes effect only
    /// after the walk has finished with it. The change is idempotent: a run that
    /// was stopped is finished by running it again.
    ///
    /// The caller is expected to have measured the tree with `scan_tree` first,
    /// so a tree deeper than [`TreeLimits::MAXIMUM_DEPTH`] was refused before
    /// the first bit changed; the depth check here is the recursion's own bound
    /// and the answer for a tree that grew since. `depth` is the number of
    /// directories above this entry within the walk, zero for the root.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::TooDeep`] past the depth ceiling,
    /// [`FilesOpError::CrossesDevice`] if a directory moved onto another
    /// filesystem between its `fstatat` and its open,
    /// [`FilesOpError::DirectoryUnusable`] when a directory cannot be read, and
    /// [`FilesOpError::MutationFailed`] when a permission change itself is
    /// refused.
    pub(crate) fn apply(
        &mut self,
        parent: &File,
        name: &OsStr,
        depth: usize,
    ) -> Result<(), FilesOpError> {
        let stat = match stat_in_directory(parent, name) {
            Ok(stat) => stat,
            // Gone since the directory was read: nothing to change, nothing skipped.
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
            Err(_) => {
                self.skip();
                return Ok(());
            }
        };

        if stat.kind == EntryKind::Symlink || stat.uid != self.uid {
            self.skip();
            return Ok(());
        }

        match stat.kind {
            EntryKind::Directory => self.apply_to_directory(parent, name, stat.device, depth),
            EntryKind::File | EntryKind::Other => self.apply_to_file(parent, name),
            // Handled above; an arm so that a new kind is a compile error here
            // and not a silent fall-through.
            EntryKind::Symlink => {
                self.skip();
                Ok(())
            }
        }
    }

    /// Changes a directory's children, then the directory.
    fn apply_to_directory(
        &mut self,
        parent: &File,
        name: &OsStr,
        device: u64,
        depth: usize,
    ) -> Result<(), FilesOpError> {
        if device != self.device {
            self.skip();
            return Ok(());
        }
        if depth >= TreeLimits::MAXIMUM_DEPTH {
            return Err(FilesOpError::TooDeep);
        }
        let directory = match open_tree_directory(parent, name, self.uid, self.device) {
            Ok(directory) => directory,
            // Unreadable to its own owner: reported, and reachable again once
            // the directory itself has been given a usable mode.
            Err(FilesOpError::DirectoryUnusable) => {
                self.skip();
                return Ok(());
            }
            Err(other) => return Err(other),
        };

        let read = list_directory_names(
            &directory,
            usize::try_from(TreeLimits::CEILING_ENTRIES)
                .unwrap_or(usize::MAX)
                .saturating_add(1),
        )
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
        for child in &read.names {
            if child == OsStr::new(".") || child == OsStr::new("..") {
                continue;
            }
            self.apply(&directory, child, depth + 1)?;
        }

        directory
            .set_permissions(Permissions::from_mode(self.directory_mode.bits()))
            .map_err(|_| FilesOpError::MutationFailed)?;
        self.change();
        Ok(())
    }

    /// Changes one file, or anything else that is not a directory or a link.
    fn apply_to_file(&mut self, parent: &File, name: &OsStr) -> Result<(), FilesOpError> {
        let Ok(entry) = open_in_directory(parent, name, ENTRY_OPEN_FLAGS) else {
            self.skip();
            return Ok(());
        };
        let judged = entry
            .metadata()
            .is_ok_and(|metadata| !metadata.is_dir() && metadata.uid() == self.uid);
        if !judged {
            self.skip();
            return Ok(());
        }
        entry
            .set_permissions(Permissions::from_mode(self.file_mode.bits()))
            .map_err(|_| FilesOpError::MutationFailed)?;
        self.change();
        Ok(())
    }
}

#[cfg(test)]
#[path = "../../tests/files/tree/chmod_tree_tests.rs"]
mod tests;
