//! The sidecar a published backup artifact is described by.

use std::fs::{File, remove_file};
use std::io::Write as _;
use std::os::unix::fs::{MetadataExt as _, OpenOptionsExt as _};
use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::publish_artifact::ARTIFACT_MODE;

/// Writes the sidecar beside the published artifact, mode `0600`, on an inode
/// this call created and then re-stated.
///
/// The sidecar is the same document the archive carries at its root, plus the
/// artifact's own digest and size. Two copies on purpose: a listing reads the
/// sidecar without decompressing gigabytes, and a restore reads the copy INSIDE
/// the archive and refuses when the two disagree — which is what makes editing
/// the cheap copy useless to anybody who tries it.
///
/// # Why the artifact's discipline, not a lighter one
///
/// Written forward from the question — *what does a reader of this file
/// decide?* — and the answer is: everything. `list_backups` reads it to say
/// what backups exist and which of them are readable, and `restore_backup`
/// reads it for the size and digest it checks the artifact against. A sidecar
/// somebody else placed is therefore not a wrong label on a right backup; it
/// is a backup an attacker gets to describe, which is the same class of
/// authority the artifact itself carries. So it gets the same two guarantees
/// the artifact gets, for the same reasons and not by analogy:
///
/// - **`create_new`**, so the inode is made by this call or the write does not
///   happen. `create` opens whatever is at the path — a symlink is FOLLOWED,
///   and `mode` applies only to a file that is created, so a pre-placed link
///   or a pre-placed `0644` file would be written through with the customer's
///   database inventory and keep its own mode. `create_new` cannot follow a
///   symlink: `O_EXCL` makes the open fail on any existing name, link
///   included.
/// - **A re-`stat` of the result** (`is_file`, uid, exact mode, `nlink == 1`),
///   for the reason `publish_artifact` states: what a path meant when the file
///   was created is not what it means now, and `nlink > 1` means a second name for
///   these bytes whose mode this one says nothing about.
///
/// **Why not the `.partial`-and-rename the artifact uses.** The
/// artifact renames because `tar` writes for minutes or hours and a reader
/// must never see a half-written archive. The sidecar is one small buffered
/// write, and a rename would actively DESTROY the property being bought here:
/// `rename` does not follow a symlink at its destination — it replaces it,
/// silently, so a pre-placed link would be quietly discarded rather than
/// refused, and the operator would never learn somebody had been in the
/// directory. Refusing is worth more here than atomicity is.
///
/// An unlink-then-create (which `create_partial` does) is refused for the
/// same reason: at this point the artifact has just been published under an id
/// no other backup will ever be minted with, so anything already wearing this
/// name is not our own litter and removing it is destroying evidence.
///
/// A refusal after publication leaves a published artifact with no sidecar.
/// That is the state `list_backups` reports as `unreadable` and
/// `delete_backup` prunes — visible litter with a name, not a silent one.
///
/// **A write that fails part-way is the other case, and it leaves nothing.**
/// Once `create_new` has succeeded the sidecar is this call's own file, and
/// `artifact` is the archive this same call published a moment ago under an id
/// no other backup is minted with. When filling or syncing the sidecar fails —
/// a disk that filled between the rename and here, `ENOSPC` (issue #148) — both
/// are removed: a half-written description beside a published archive is the
/// half-written artefact the operation promises never to leave, and the backup
/// is reported failed as a whole.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`], because a published artifact
/// nothing can describe is not a backup the panel may report.
pub(super) fn write_sidecar(
    sidecar: &Path,
    artifact: &Path,
    summary: &BackupSummary,
    owner: u32,
) -> Result<(), BackupError> {
    let rendered =
        serde_json::to_vec_pretty(summary).map_err(|_| BackupError::ArtifactUnpublishable)?;

    let mut file = File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(sidecar)
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    let filled = file.write_all(&rendered).and_then(|()| file.sync_all());
    drop(file);
    if filled.is_err() {
        let _ = remove_file(sidecar);
        let _ = remove_file(artifact);
        return Err(BackupError::ArtifactUnpublishable);
    }

    verify_sidecar_inode(sidecar, owner)
}

/// Asks the inode what the sidecar this call just wrote actually is, and
/// removes it rather than leaving it when the answer is wrong.
///
/// Split from [`write_sidecar`] so the descriptor is closed before the path is
/// stated — the same question `publish_artifact` asks, asked of a file that has no
/// rename to gate it, so the removal takes the rename's place as the thing
/// that stops a wrong inode being advertised as this backup's description.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
fn verify_sidecar_inode(sidecar: &Path, owner: u32) -> Result<(), BackupError> {
    let metadata = sidecar
        .symlink_metadata()
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    let safe = metadata.is_file()
        && metadata.uid() == owner
        && metadata.mode() & 0o7777 == ARTIFACT_MODE
        && metadata.nlink() == 1;
    if !safe {
        let _ = remove_file(sidecar);
        return Err(BackupError::ArtifactUnpublishable);
    }

    Ok(())
}
