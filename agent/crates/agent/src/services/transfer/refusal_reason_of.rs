//! Which refusal an import's failure was, for the panel to tell apart.

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_ops::backup::BackupError;

use crate::proto::ImportRefusalReason;

/// The reason a failed import is reported with, or unspecified for a failure of
/// the host itself.
///
/// A match with a wildcard and not an exhaustive one, on purpose: `BackupError`
/// is `#[non_exhaustive]` and holds every backup failure, and a failure this
/// function does not name is by definition not one of the refusals the panel acts
/// on differently.
#[must_use]
pub fn refusal_reason_of(error: &BackupError) -> ImportRefusalReason {
    match error {
        BackupError::PackageSignatureInvalid => ImportRefusalReason::SignatureInvalid,
        BackupError::PackageFingerprintMismatch => ImportRefusalReason::FingerprintMismatch,
        BackupError::PackageManifestMalformed => ImportRefusalReason::ManifestMalformed,
        BackupError::ImportAccountExists => ImportRefusalReason::AccountExists,
        BackupError::ImportBackupIdTaken => ImportRefusalReason::BackupIdTaken,
        // Only a measured shortage is "no room". A filesystem that could not be
        // measured is a fault of this host (the status mapping says the same), and
        // telling the operator the package is too large would send them to shrink
        // a package that is fine.
        BackupError::ImportTooLarge { .. }
        | BackupError::FreeSpace(FloorRefusal::BelowFloor { .. }) => ImportRefusalReason::TooLarge,
        BackupError::ImportLengthMismatch => ImportRefusalReason::LengthMismatch,
        BackupError::ImportDigestMismatch => ImportRefusalReason::DigestMismatch,
        BackupError::SealedArtifactNotTransferable
        | BackupError::UnexpectedArchiveMember { .. }
        | BackupError::ManifestUnreadable
        | BackupError::ManifestVersionUnknown
        | BackupError::ManifestAccountMismatch
        | BackupError::ManifestBackupMismatch
        | BackupError::ManifestDisagreesWithSidecar
        | BackupError::ManifestTooLarge
        | BackupError::MemberNotRegularFile
        | BackupError::DumpMissing => ImportRefusalReason::ArtifactRefused,
        _ => ImportRefusalReason::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/refusal_reason_of_tests.rs"]
mod tests;
