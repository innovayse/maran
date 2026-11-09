//! Copying a file or a tree inside a customer's home, as the customer.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::fs::MetadataExt;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::transfer_bounds::TransferBounds;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::io_failure::io_failure;
use crate::files::model::copy_report::CopyReport;
use crate::files::model::missing_parents::MissingParents;
use crate::files::mutable_path_components::mutable_path_components;
use crate::files::open_home::{DIRECTORY_FLAGS, open_home};
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::refuse_taken_name::refuse_taken_name;
use crate::files::remove_trash_entry::remove_trash_entry;
use crate::files::rename_failure::rename_failure;
use crate::files::tree::copy_tree::CopyTree;
use crate::files::tree::scan_tree::scan_tree;
use crate::files::tree::tree_limits::TreeLimits;
use crate::files::tree::tree_tally::TreeTally;

/// Permission bits of a staging directory.
const STAGING_MODE: libc::mode_t = 0o700;

/// Name of the copy inside its staging directory.
const STAGED_NAME: &str = "copy";

/// Copies `source` to `destination` inside `home`, as the account owning `uid`.
///
/// **All or nothing at the destination.** The tree is measured first
/// (`scan_tree`) and refused as a whole, before anything is written, when it
/// holds more than the stated entries or bytes, is deeper than the ceiling, or
/// holds anything but regular files and directories: a symlink, FIFO, socket or
/// device anywhere in it, or a directory on another filesystem or owned by
/// somebody else, refuses the copy. Then it is copied into
/// `<home>/.maran-trash/staging/<stage>/copy` (`stage` is built by the caller in
/// the parent) and that is renamed to the destination with
/// `renameat2(RENAME_NOREPLACE)`, so a copy that fails, is stopped or is killed
/// part-way leaves nothing visible at the destination; the staging directory of
/// a failed run is removed here and one a kill left behind is swept by the
/// trash purge.
///
/// A destination that exists is [`FilesOpError::AlreadyExists`], checked early
/// for a typed answer and again by the no-replace rename for the instant
/// between; a missing source is [`FilesOpError::NotFound`]; a destination
/// inside the source is [`FilesOpError::IntoItself`]. The destination's missing
/// parents are created. The staging area is in the home, so the final rename
/// cannot cross onto another filesystem unless the destination is on a mount
/// inside the home, which is [`FilesOpError::CrossesDevice`] and leaves nothing.
/// The caller applies the free-space floor before the first byte.
///
/// Permission bits are kept (masked to `0o777`); owner and times are not.
/// The bytes counted are those actually copied.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do; the verdicts above; [`FilesOpError::LimitReached`],
/// [`FilesOpError::TooDeep`], [`FilesOpError::UnsupportedEntry`],
/// [`FilesOpError::CrossesDevice`] and [`FilesOpError::NotOwned`] from the
/// measuring; and [`FilesOpError::QuotaExceeded`], [`FilesOpError::WriteFailed`]
/// and [`FilesOpError::MutationFailed`] from the copy and the final rename.
pub(crate) fn copy_in_home(
    home: &Path,
    source: &MutablePath,
    destination: &MutablePath,
    bounds: &TransferBounds,
    stage: &TrashEntryId,
    uid: u32,
) -> Result<CopyReport, FilesOpError> {
    let source_parent = open_mutable_parent(home, source, uid, MissingParents::Require)?;
    let source_name = OsStr::new(source.file_name());
    let stat = match stat_in_directory(&source_parent, source_name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    };
    if matches!(stat.kind, EntryKind::Symlink | EntryKind::Other) {
        return Err(FilesOpError::UnsupportedEntry);
    }

    let from = mutable_path_components(source);
    let to = mutable_path_components(destination);
    if from == to {
        return Err(FilesOpError::AlreadyExists);
    }
    if to.starts_with(&from) {
        return Err(FilesOpError::IntoItself);
    }

    let destination_parent = open_mutable_parent(home, destination, uid, MissingParents::Create)?;
    let destination_name = OsStr::new(destination.file_name());
    refuse_taken_name(&destination_parent, destination_name)?;

    let limits = TreeLimits::stated(bounds.max_entries(), bounds.max_bytes());
    let mut measured = TreeTally::default();
    scan_tree(
        &source_parent,
        source_name,
        uid,
        stat.device,
        0,
        &limits,
        &mut measured,
    )?;
    if measured.unsupported > 0 {
        return Err(FilesOpError::UnsupportedEntry);
    }
    if measured.crossings > 0 {
        return Err(FilesOpError::CrossesDevice);
    }
    if measured.foreign > 0 {
        return Err(FilesOpError::NotOwned);
    }

    let home_directory = open_home(home, uid)?;
    let staging = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME,
        uid,
        MissingParents::Create,
    )?
    .ok_or(FilesOpError::DirectoryUnusable)?;

    let stage_name = OsStr::new(stage.as_str());
    make_directory_in_directory(&staging, stage_name, STAGING_MODE)
        .map_err(|error| io_failure(&error, FilesOpError::MutationFailed))?;

    let mut copy = CopyTree::new(uid, stat.device, limits);
    let finished = stage_and_place(
        &staging,
        stage_name,
        (&source_parent, source_name),
        (&destination_parent, destination_name),
        &mut copy,
    );
    // Whatever is left of the staging directory goes, success or not: on
    // success it is empty (its one child was renamed out), on failure it holds
    // the partial copy that must never be visible.
    let _ = remove_trash_entry(&staging, stage_name, uid);

    finished?;
    let copied = copy.tally();
    Ok(CopyReport {
        entries: copied.entries,
        bytes: copied.bytes,
    })
}

/// Copies into the staging directory, then renames the copy to its destination.
///
/// `from` and `to` are each a held directory and an entry name inside it. Split
/// from `copy_in_home` so the caller has one place to hang the removal of the
/// staging directory on.
///
/// # Errors
///
/// As `CopyTree::copy` and `rename_failure`, and
/// [`FilesOpError::MutationFailed`] when the staging directory just made is not
/// a directory the account owns.
fn stage_and_place(
    staging: &File,
    stage_name: &OsStr,
    from: (&File, &OsStr),
    to: (&File, &OsStr),
    copy: &mut CopyTree,
) -> Result<(), FilesOpError> {
    let stage = open_in_directory(staging, stage_name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::MutationFailed)?;
    let metadata = stage.metadata().map_err(|_| FilesOpError::MutationFailed)?;
    if !metadata.is_dir() || metadata.uid() != copy.uid() {
        return Err(FilesOpError::MutationFailed);
    }

    let staged = OsStr::new(STAGED_NAME);
    copy.copy(from.0, from.1, &stage, staged, 0)?;

    rename_no_replace_between_directories(&stage, staged, to.0, to.1)
        .map_err(|error| rename_failure(&error))
}

#[cfg(test)]
#[path = "../tests/files/copy_in_home_tests.rs"]
mod tests;
