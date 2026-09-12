//! Putting the swap marker on the disk, durably, and taking it off again.

use std::fs::{File, OpenOptions, remove_file};
use std::io::Write as _;
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::restore_marker::{RESTORE_MARKER_VERSION, RestoreMarker};

/// The suffix every marker's file name carries.
///
/// The staging root also holds `<account>.<id>` and `<account>.previous.<id>`
/// directories; a backup id is a uuid, so neither can end in this. The suffix is
/// how the reconciliation finds candidates to OPEN — it is never how it decides
/// anything, which is the document's job.
pub(crate) const MARKER_SUFFIX: &str = ".swap";

/// The suffix a marker carries while it is being written.
///
/// The marker is written under this name, made durable, and then renamed to
/// [`MARKER_SUFFIX`], so a marker that exists under its real name is a marker
/// whose bytes are all there. One left under THIS name is a write that was
/// itself interrupted — which means the first `rename` of the swap had not
/// happened, because it happens strictly after — so the reconciliation removes
/// it and does nothing else.
pub(crate) const PARTIAL_MARKER_SUFFIX: &str = ".swap.partial";

/// The mode a marker is created with: root's alone, read and write.
const MARKER_MODE: u32 = 0o600;

/// Where `account`'s marker for `backup_id` lives.
///
/// In the staging root, beside the two directories it describes, and on the same
/// filesystem as both — so a marker cannot be durable on a disk whose swap is
/// not, or the reverse.
pub(crate) fn restore_marker_path(account: &AccountName, backup_id: &BackupId) -> PathBuf {
    PathBuf::from(AgentPaths::RESTORE_STAGING_ROOT).join(format!(
        "{}.{}{MARKER_SUFFIX}",
        account.as_str(),
        backup_id.as_str()
    ))
}

/// Writes the marker and makes it durable, before the caller's first `rename`.
///
/// # Why the durability dance and not one `write`
///
/// The whole value of this file is that it is on the disk **before** the home
/// moves. A `write` that is still in the page cache when the power goes is a
/// file that never existed, and what survives is then the rename with no record
/// of who it belonged to — precisely the unrecoverable state this marker exists
/// to remove. So: write to a temporary name, `sync_all` the file, `rename` it
/// into place, and `sync_all` the DIRECTORY, which is what makes the new
/// directory entry itself durable. Omitting the last step is the classic version
/// of this mistake, and it fails only under a power cut, which is the one
/// condition no test here reproduces.
///
/// The temporary file is in the same directory as the target, because a `rename`
/// is atomic only within one filesystem.
///
/// # Why the path is a parameter and not read from [`restore_marker_path`]
///
/// The same reason `Placement` injects every other path a restore touches: a
/// test owns nothing under `/home`, and an operation that read the real constant
/// could not be exercised at all. `restore_backup` fills it from
/// [`restore_marker_path`] and from nowhere else.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] for any failure. At the point this is called
/// nothing has been renamed, so a failure here leaves the account exactly as it
/// was — which is why one variant covers every cause: the answer to all of them
/// is the same.
pub(crate) fn write_restore_marker(
    path: &Path,
    account: &AccountName,
    backup_id: &BackupId,
    owner_uid: u32,
    home_gid: u32,
    home_mode: u32,
) -> Result<(), BackupError> {
    let marker = RestoreMarker {
        version: RESTORE_MARKER_VERSION,
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        owner_uid,
        home_gid,
        home_mode,
    };
    let encoded = serde_json::to_vec(&marker).map_err(|_error| BackupError::StagingUnusable)?;

    let directory = path.parent().ok_or(BackupError::StagingUnusable)?;
    let staging = path.with_extension("swap.partial");
    debug_assert!(
        staging.to_string_lossy().ends_with(PARTIAL_MARKER_SUFFIX),
        "the partial name and the suffix the reconciliation sweeps must agree"
    );

    let mut file = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(true)
        .mode(MARKER_MODE)
        .open(&staging)
        .map_err(|_error| BackupError::StagingUnusable)?;
    file.write_all(&encoded)
        .map_err(|_error| BackupError::StagingUnusable)?;
    file.sync_all()
        .map_err(|_error| BackupError::StagingUnusable)?;
    drop(file);

    std::fs::rename(&staging, path).map_err(|_error| BackupError::StagingUnusable)?;

    File::open(directory)
        .and_then(|handle| handle.sync_all())
        .map_err(|_error| BackupError::StagingUnusable)?;

    Ok(())
}

/// Takes the marker off the disk once the swap has fully finished.
///
/// Best effort, and deliberately not an error: at the point this is called the
/// restore has succeeded, and a marker left behind is not a customer who cannot
/// work — it is one extra reconciliation on the next start, which finds the
/// swap already `Completed` and removes the marker then. That is
/// `SwapState::Completed`, and it is a case with a test.
pub(crate) fn remove_restore_marker(path: &Path) {
    let _ = remove_file(path);
}

/// Reads a marker back, or refuses the bytes at `path`.
///
/// Refuses rather than repairs, on the version as well as the shape: a document
/// a future build wrote is one this build does not understand, and acting on
/// half of it means a root `rename` made on a guess.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] when the file cannot be read, is not this
/// agent's JSON, or carries a version this build does not know.
pub(crate) fn read_restore_marker(path: &Path) -> Result<RestoreMarker, BackupError> {
    let bytes = std::fs::read(path).map_err(|_error| BackupError::StagingUnusable)?;
    let marker: RestoreMarker =
        serde_json::from_slice(&bytes).map_err(|_error| BackupError::StagingUnusable)?;

    if marker.version != RESTORE_MARKER_VERSION {
        return Err(BackupError::StagingUnusable);
    }

    Ok(marker)
}

#[cfg(test)]
#[path = "../tests/backup/restore_marker_file_tests.rs"]
mod tests;
