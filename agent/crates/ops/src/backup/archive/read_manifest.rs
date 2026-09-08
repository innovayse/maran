//! Reading the manifest out of an archive, before anything trusts the archive.

use std::fs::read;
use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::backup_manifest::{BackupManifest, MANIFEST_VERSION};
use crate::backup::model::extract_spec::ExtractSpec;

/// The manifest's name, at the archive's root and in the scratch it lands in.
const MANIFEST_FILE: &str = "manifest.json";

/// Extracts `manifest.json` root-side and answers what it says.
///
/// # Why root-side, when the home is not
///
/// For the same reason the dumps are (R4): this document decides which
/// databases a restore is about to drop, and a decision read out of
/// account-writable space is a decision the account made. It goes into the
/// root-only scratch, which nothing but this agent can reach.
///
/// It is also the FIRST thing extracted, before the dumps and long before the
/// home, because everything after it is conditional on what it says.
///
/// # What it refuses
///
/// - A version this agent does not know
///   ([`BackupError::ManifestVersionUnknown`]). Refused rather than read on the
///   fields it recognises: a manifest whose newer version added a database this
///   reader ignores is a restore that silently leaves that database alone.
/// - A manifest naming another account
///   ([`BackupError::ManifestAccountMismatch`]). An artifact is addressed to
///   one account, and restoring one account's databases over another's is the
///   single worst thing this operation could be talked into.
///
/// # Errors
///
/// The two above, plus [`BackupError::ArchiveFailed`] when the member cannot be
/// extracted and [`BackupError::ManifestUnwritable`] when what landed cannot be
/// read or parsed — the same variant the create side uses for the same
/// document, because "this agent and this manifest could not agree" is one
/// condition whichever direction it is met from.
pub(crate) fn read_manifest(
    host: &dyn BackupHost,
    artifact: &Path,
    scratch: &Path,
    account: &str,
) -> Result<BackupManifest, BackupError> {
    host.extract(&ExtractSpec {
        artifact: artifact.to_path_buf(),
        into: scratch.to_path_buf(),
        part: ArchivePart::Manifest,
    })?;

    let bytes = read(scratch.join(MANIFEST_FILE)).map_err(|_| BackupError::ManifestUnwritable)?;
    let manifest: BackupManifest =
        serde_json::from_slice(&bytes).map_err(|_| BackupError::ManifestUnwritable)?;

    if manifest.version != MANIFEST_VERSION {
        return Err(BackupError::ManifestVersionUnknown);
    }
    if manifest.account != account {
        return Err(BackupError::ManifestAccountMismatch);
    }

    Ok(manifest)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/read_manifest_tests.rs"]
mod tests;
