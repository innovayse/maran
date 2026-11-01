//! Publishing a finished backup artifact onto its final name.

use std::fs::rename;
use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;

/// The mode the artifact and its sidecar are created with, and the only one
/// they may carry when they are published.
///
/// `0600`, root's alone, and it is a rules/security.md item 8 decision rather
/// than a convenience. The artifact holds every file in the customer's home and
/// a full dump of every database they own — which is to say every credential,
/// token and personal record their application stores. The directory above it
/// is already root-only, so this is the second of two locks, not the first; it
/// is here because a directory's mode is one `chmod` away from being wrong and
/// the artifact is the thing that must not be readable when that happens. The
/// owner is root for the same reason it is not the account: an account that
/// could read its own artifact could replace it, and a restore reads it as
/// root.
pub(super) const ARTIFACT_MODE: u32 = 0o600;

/// Checks the finished artifact's own inode and publishes it by rename.
///
/// The mode and owner are checked HERE, on the file the archiver actually
/// wrote, and not assumed from the mode it was created with: `tar` was handed a
/// path, and what a path meant when the file was created is not what it means
/// now. An artifact that is not root's alone is not published at all — the
/// rename would make a readable copy of a customer's database the panel then
/// advertises as a backup.
///
/// `nlink` is checked for the same reason the file operations area checks it: a
/// second hard link to the artifact is a second name for the same bytes, and
/// the mode on this name says nothing about who can open the other one.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
pub(super) fn publish_artifact(
    partial: &Path,
    artifact: &Path,
    owner: u32,
) -> Result<(), BackupError> {
    let metadata = partial
        .symlink_metadata()
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    let safe = metadata.is_file()
        && metadata.uid() == owner
        && metadata.mode() & 0o7777 == ARTIFACT_MODE
        && metadata.nlink() == 1;
    if !safe {
        return Err(BackupError::ArtifactUnpublishable);
    }

    rename(partial, artifact).map_err(|_| BackupError::ArtifactUnpublishable)
}
