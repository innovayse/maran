//! Writing the manifest into the scratch and running the archiver over both.

use std::fs::File;
use std::io::Write as _;
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::backup_manifest::BackupManifest;

/// The manifest's name, at the archive's root. Part of the contract (R2).
const MANIFEST_FILE: &str = "manifest.json";

/// The mode the manifest is written with inside the root-only scratch.
///
/// `0600` for the same reason the artifact is: the manifest lists a customer's
/// database names, and the directory it sits in is root-only already — this is
/// the second of the two locks rather than the first.
const MANIFEST_MODE: u32 = 0o600;

/// Writes `manifest` into the scratch and archives the home together with it.
///
/// The two are one step because they are one moment: the manifest has to be in
/// the scratch at the instant `tar` reads that directory, and separating them
/// into two callers would make "did the manifest get in" a question each caller
/// answers for itself.
///
/// The manifest is written pretty-printed. It is a document an operator opens
/// in a rescue shell when the panel is gone, and the bytes it costs are
/// nothing beside the archive it describes.
///
/// # Errors
///
/// - [`BackupError::ManifestUnwritable`] when the manifest cannot be serialised
///   or written into the scratch.
/// - [`BackupError::ArchiveFailed`] when the archiver refuses or cannot be run.
pub(crate) fn archive_home(
    host: &dyn BackupHost,
    manifest: &BackupManifest,
    spec: &ArchiveSpec,
) -> Result<u64, BackupError> {
    write_manifest(manifest, &spec.scratch)?;
    host.create_archive(spec)
}

/// Serialises `manifest` into `<scratch>/manifest.json`, mode `0600`.
///
/// # Errors
///
/// Returns [`BackupError::ManifestUnwritable`] for a manifest that cannot be
/// serialised, a file that cannot be created, and a write that does not
/// complete — the last one included, because a truncated manifest is a document
/// a restore would refuse to parse only if it is lucky.
fn write_manifest(manifest: &BackupManifest, scratch: &Path) -> Result<(), BackupError> {
    let rendered =
        serde_json::to_vec_pretty(manifest).map_err(|_| BackupError::ManifestUnwritable)?;

    let mut file: File = File::options()
        .write(true)
        .create_new(true)
        .mode(MANIFEST_MODE)
        .open(scratch.join(MANIFEST_FILE))
        .map_err(|_| BackupError::ManifestUnwritable)?;

    file.write_all(&rendered)
        .map_err(|_| BackupError::ManifestUnwritable)?;
    file.sync_all().map_err(|_| BackupError::ManifestUnwritable)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/archive_home_tests.rs"]
mod tests;
