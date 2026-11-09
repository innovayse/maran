//! Changing permission bits inside a customer's home, as the customer.

use std::ffi::OsStr;
use std::fs::Permissions;
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::path::Path;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;
use crate::files::entry_open_flags::ENTRY_OPEN_FLAGS;
use crate::files::model::missing_parents::MissingParents;
use crate::files::model::permission_report::PermissionReport;
use crate::files::open_home::DIRECTORY_FLAGS;
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::tree::chmod_tree::ChmodTree;
use crate::files::tree::scan_tree::scan_tree;
use crate::files::tree::tree_limits::TreeLimits;
use crate::files::tree::tree_tally::TreeTally;

/// Sets permission bits on `path` inside `home`, as the account owning `uid`.
///
/// Without `recursive` the one entry gets `mode`, whatever it is (a directory
/// included). With it a directory's whole tree is walked by `ChmodTree`: files
/// get `mode`, directories get `directory_mode`, so one call can leave files
/// `0644` and directories `0755`, and a recursive `0644` that would make every
/// directory untraversable is a choice the caller has to make by passing it
/// twice. `recursive` on something that is not a directory is the same as not
/// recursive: there is no tree.
///
/// The change is an `fchmod` on a descriptor opened without following links,
/// after an `fstat` of that descriptor, so a symlink as the path is
/// [`FilesOpError::UnsupportedEntry`] and its target is never touched, and an
/// entry the account does not own is [`FilesOpError::NotOwned`]. Inside a tree
/// the same two cases are skipped and counted instead (see `ChmodTree`).
///
/// A recursive change measures the tree first with `scan_tree`, so a tree deeper
/// than the ceiling, or with more entries than the agent will walk, is refused as
/// a whole before the first bit changes. Setting the same modes twice is success
/// both times.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as `open_mutable_parent` does; [`FilesOpError::NotFound`],
/// [`FilesOpError::UnsupportedEntry`] and [`FilesOpError::NotOwned`] for the
/// root; [`FilesOpError::TooDeep`] and [`FilesOpError::LimitReached`] from the
/// measuring; and [`FilesOpError::MutationFailed`] when a change is refused.
pub(crate) fn chmod_in_home(
    home: &Path,
    path: &MutablePath,
    mode: FileMode,
    directory_mode: FileMode,
    recursive: bool,
    uid: u32,
) -> Result<PermissionReport, FilesOpError> {
    let parent = open_mutable_parent(home, path, uid, MissingParents::Require)?;
    let name = OsStr::new(path.file_name());

    let stat = match stat_in_directory(&parent, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    };
    if stat.kind == EntryKind::Symlink {
        return Err(FilesOpError::UnsupportedEntry);
    }
    if stat.uid != uid {
        return Err(FilesOpError::NotOwned);
    }

    if recursive && stat.kind == EntryKind::Directory {
        let mut tally = TreeTally::default();
        scan_tree(
            &parent,
            name,
            uid,
            stat.device,
            0,
            &TreeLimits::ceilings(),
            &mut tally,
        )?;

        let mut walk = ChmodTree::new(mode, directory_mode, uid, stat.device);
        walk.apply(&parent, name, 0)?;
        return Ok(walk.report());
    }

    // One entry. A directory is opened as a directory and anything else as the
    // non-blocking, non-following open every entry gets, then judged by `fstat`
    // on what was actually opened.
    let flags = if stat.kind == EntryKind::Directory {
        DIRECTORY_FLAGS
    } else {
        ENTRY_OPEN_FLAGS
    };
    let entry =
        open_in_directory(&parent, name, flags).map_err(|_| FilesOpError::MutationFailed)?;
    let metadata = entry.metadata().map_err(|_| FilesOpError::MutationFailed)?;
    if metadata.uid() != uid {
        return Err(FilesOpError::NotOwned);
    }
    entry
        .set_permissions(Permissions::from_mode(mode.bits()))
        .map_err(|_| FilesOpError::MutationFailed)?;

    Ok(PermissionReport {
        changed: 1,
        skipped: 0,
    })
}

#[cfg(test)]
#[path = "../tests/files/chmod_in_home_tests.rs"]
mod tests;
