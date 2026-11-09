//! Judging the fields of a manifest a peer chose.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

const DIGEST: &str = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

fn manifest() -> TransferManifest {
    TransferManifest {
        version: TRANSFER_MANIFEST_VERSION,
        account: "alice".to_owned(),
        backup_id: "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b".to_owned(),
        artifact_bytes: 10,
        artifact_sha256: DIGEST.to_owned(),
        home_bytes: 5,
        databases: vec!["alice_shop".to_owned()],
        created_at_unix: 1,
        agent_version: "0.0.0".to_owned(),
        signer_fingerprint: DIGEST.to_owned(),
    }
}

#[test]
fn a_well_formed_manifest_yields_its_account_and_backup_id() {
    let (account, backup_id) = manifest().validated().unwrap();

    assert_eq!(account.as_str(), "alice");
    assert_eq!(backup_id.as_str(), "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b");
}

#[test]
fn every_malformed_field_is_a_refusal() {
    let cases: Vec<(&str, TransferManifest)> = vec![
        (
            "version",
            TransferManifest {
                version: 9,
                ..manifest()
            },
        ),
        (
            "empty artifact",
            TransferManifest {
                artifact_bytes: 0,
                ..manifest()
            },
        ),
        (
            "uppercase digest",
            TransferManifest {
                artifact_sha256: DIGEST.to_uppercase(),
                ..manifest()
            },
        ),
        (
            "short digest",
            TransferManifest {
                artifact_sha256: "abc".to_owned(),
                ..manifest()
            },
        ),
        (
            "bad fingerprint",
            TransferManifest {
                signer_fingerprint: "zz".to_owned(),
                ..manifest()
            },
        ),
        (
            "account",
            TransferManifest {
                account: "../etc".to_owned(),
                ..manifest()
            },
        ),
        (
            "backup id",
            TransferManifest {
                backup_id: "../../x".to_owned(),
                ..manifest()
            },
        ),
        (
            "another account's database",
            TransferManifest {
                databases: vec!["bobby_shop".to_owned()],
                ..manifest()
            },
        ),
        (
            "too many databases",
            TransferManifest {
                databases: vec!["alice_shop".to_owned(); MAXIMUM_DATABASES + 1],
                ..manifest()
            },
        ),
    ];

    for (label, case) in cases {
        assert_eq!(
            case.validated().map(|_| ()),
            Err(BackupError::PackageManifestMalformed),
            "{label}"
        );
    }
}
