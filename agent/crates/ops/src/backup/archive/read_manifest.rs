//! Reading the manifest out of an archive, before anything trusts the archive.

use std::fs::read;
use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::backup_manifest::{BackupManifest, MANIFEST_VERSION};
use crate::backup::model::extract_spec::ExtractSpec;

/// The manifest's name, at the archive's root and in the scratch it lands in.
const MANIFEST_FILE: &str = "manifest.json";

/// The largest manifest this agent will read: 4 MiB.
///
/// A manifest holds one short line per database plus a handful of scalars, so a
/// real one is a few kilobytes and one for an account with thousands of
/// databases is well under a megabyte. The ceiling is far above any of those and
/// far below anything that would matter to a root process reading it whole; it
/// exists so that an artifact that has been replaced cannot make the agent
/// allocate what its manifest member says.
const MANIFEST_CEILING_BYTES: u64 = 4 * 1024 * 1024;

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
/// - A manifest member that did not come out as a plain file with a single link
///   ([`BackupError::MemberNotRegularFile`]). `tar` extracts a symbolic link
///   member as a symbolic link, and reading it would follow the link as root;
///   the member is judged by what the extraction left on disk, with
///   `symlink_metadata`, before any byte of it is read. The layout scan cannot
///   catch this, because it decides on names.
/// - A manifest larger than `MANIFEST_CEILING_BYTES`
///   ([`BackupError::ManifestTooLarge`]), refused before it is parsed.
///
/// # Errors
///
/// The ones above, plus [`BackupError::ArchiveFailed`] when the member cannot be
/// extracted, [`BackupError::Bound`] when the archiver was killed at its
/// deadline, and [`BackupError::ManifestUnwritable`] when what landed cannot be
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

    let extracted = scratch.join(MANIFEST_FILE);
    let metadata = extracted
        .symlink_metadata()
        .map_err(|_| BackupError::ManifestUnwritable)?;
    if !metadata.is_file() || metadata.nlink() != 1 {
        return Err(BackupError::MemberNotRegularFile);
    }
    if metadata.len() > MANIFEST_CEILING_BYTES {
        return Err(BackupError::ManifestTooLarge);
    }

    let bytes = read(&extracted).map_err(|_| BackupError::ManifestUnwritable)?;
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
