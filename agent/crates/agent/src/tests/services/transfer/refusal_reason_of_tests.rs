//! Which refusals the panel is told apart by, and that a host fault is not one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::floor_refusal::FloorRefusal;

use super::*;

#[test]
fn each_refusal_the_panel_acts_on_has_its_own_reason() {
    let cases = [
        (
            BackupError::PackageSignatureInvalid,
            ImportRefusalReason::SignatureInvalid,
        ),
        (
            BackupError::PackageFingerprintMismatch,
            ImportRefusalReason::FingerprintMismatch,
        ),
        (
            BackupError::PackageManifestMalformed,
            ImportRefusalReason::ManifestMalformed,
        ),
        (
            BackupError::ImportAccountExists,
            ImportRefusalReason::AccountExists,
        ),
        (
            BackupError::ImportBackupIdTaken,
            ImportRefusalReason::BackupIdTaken,
        ),
        (
            BackupError::ImportTooLarge {
                declared: 2,
                limit: 1,
            },
            ImportRefusalReason::TooLarge,
        ),
        (
            BackupError::FreeSpace(FloorRefusal::BelowFloor {
                available: 1,
                floor: 2,
            }),
            ImportRefusalReason::TooLarge,
        ),
        (
            BackupError::ImportLengthMismatch,
            ImportRefusalReason::LengthMismatch,
        ),
        (
            BackupError::ImportDigestMismatch,
            ImportRefusalReason::DigestMismatch,
        ),
        (
            BackupError::SealedArtifactNotTransferable,
            ImportRefusalReason::ArtifactRefused,
        ),
        (
            BackupError::DumpMissing,
            ImportRefusalReason::ArtifactRefused,
        ),
        (
            BackupError::ManifestDisagreesWithSidecar,
            ImportRefusalReason::ArtifactRefused,
        ),
        (
            BackupError::UnexpectedArchiveMember {
                name: "x".to_owned(),
            },
            ImportRefusalReason::ArtifactRefused,
        ),
    ];

    for (error, reason) in cases {
        assert_eq!(refusal_reason_of(&error), reason, "{error}");
    }
}

#[test]
fn a_fault_of_the_host_has_no_refusal_reason() {
    for error in [
        BackupError::ImportInterrupted,
        BackupError::ArtifactUnpublishable,
        BackupError::ScratchUnusable,
        BackupError::AlreadyRunning,
        BackupError::FreeSpace(FloorRefusal::Unmeasurable),
    ] {
        assert_eq!(
            refusal_reason_of(&error),
            ImportRefusalReason::Unspecified,
            "{error}"
        );
    }
}
