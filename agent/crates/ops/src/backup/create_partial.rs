//! The `.partial` file a backup's archiver writes into.

use std::fs::{File, remove_file};
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::publish_artifact::ARTIFACT_MODE;

/// Creates the `.partial` the archiver writes, mode `0600`, and refuses a name
/// that is already taken.
///
/// `create_new`, so the file is made by this call or not at all: the archiver
/// is handed a descriptor-backed name this operation created with the mode it
/// chose, rather than opening whatever is at that path. A `.partial` left by a
/// crashed run is removed by the operation's own cleanup, and one that is still
/// there at this moment means something else is writing it — which the account
/// lock says cannot be another backup.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
pub(super) fn create_partial(partial: &Path) -> Result<(), BackupError> {
    let _ = remove_file(partial);

    File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(partial)
        .map(drop)
        .map_err(|_| BackupError::ArtifactUnpublishable)
}
