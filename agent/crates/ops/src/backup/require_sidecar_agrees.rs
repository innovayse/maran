//! The restore's refusal of an archive whose sidecar and manifest disagree.

use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::read_sidecar::read_sidecar;

/// Refuses an archive whose sidecar and internal manifest do not say the same
/// thing.
///
/// Two copies of one document exist on purpose: a listing reads the sidecar
/// without decompressing gigabytes, and a restore reads the copy inside the
/// archive. Comparing them is what makes editing the cheap copy useless — a
/// sidecar claiming a smaller database list, or another digest, is refused
/// rather than believed.
///
/// A sidecar that cannot be read or parsed is refused too, and not treated as
/// "no second opinion available". A check that reports "safe" when it could not
/// observe anything is the check this repository has already been burned by.
/// That refusal is now structural as well as written down: `read_sidecar`
/// answers a `ReadableBackup`, whose manifest and digest are not optional, so
/// there is no half-empty value for this comparison to be made against.
///
/// Read through `read_sidecar`, the same function
/// [`list_backups`](crate::backup::list_backups) uses: a sidecar naming a
/// version this agent does not understand is refused right there, before its
/// `manifest` or `artifact_sha256` field is ever compared to anything. That
/// used not to be true — this function used to parse the sidecar on its own
/// and never look at its version at all, so a listing could refuse an
/// artifact as unreadable while a restore accepted the very same sidecar.
/// Going through the one shared reader closes that gap here and for whatever
/// third caller reads a sidecar next.
///
/// # Errors
///
/// [`BackupError::ManifestDisagreesWithSidecar`], including when the sidecar
/// names a version this agent does not understand.
pub(super) fn require_sidecar_agrees(
    sidecar: &Path,
    manifest: &BackupManifest,
    artifact_sha256: &str,
) -> Result<(), BackupError> {
    // A sidecar that describes nothing is a disagreement, not an absence of
    // one — this check reports on what it could observe, and a sidecar it
    // could not read is no second opinion at all. `read_sidecar` answers a
    // `ReadableBackup`, so that case cannot arrive here as a value with
    // nothing in it: it arrives as the `Err` this line refuses.
    let details =
        read_sidecar(sidecar).map_err(|_reason| BackupError::ManifestDisagreesWithSidecar)?;

    let agrees =
        &details.manifest == manifest && details.artifact_sha256.as_str() == artifact_sha256;
    if !agrees {
        return Err(BackupError::ManifestDisagreesWithSidecar);
    }

    Ok(())
}
