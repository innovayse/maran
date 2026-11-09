//! What an `ExportBackup` request must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::proto::ErrorCode;

/// An inert P-384 key generated for these tests; it signs nothing real.
const KEY: &[u8] = include_bytes!("../../../../tests/fixtures/package_signing_key.pkcs8.der");

fn request() -> ExportBackupRequest {
    ExportBackupRequest {
        account_username: "alice".to_owned(),
        backup_id: "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b".to_owned(),
        signing_key: KEY.to_vec(),
    }
}

#[test]
fn a_well_formed_request_yields_the_account_the_id_and_the_key() {
    let (account, backup_id, key) = validated_export(request()).unwrap();

    assert_eq!(account.as_str(), "alice");
    assert_eq!(backup_id.as_str(), "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b");
    assert_eq!(key.public_key().len(), 97);
}

#[test]
fn a_bad_account_or_backup_id_is_invalid_input() {
    for case in [
        ExportBackupRequest {
            account_username: "../root".to_owned(),
            ..request()
        },
        ExportBackupRequest {
            backup_id: "../../x".to_owned(),
            ..request()
        },
    ] {
        let error = validated_export(case).unwrap_err();
        assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_key_that_is_not_an_ecdsa_p384_private_key_is_invalid_input_and_never_echoed() {
    for key in [
        Vec::new(),
        b"-----BEGIN PRIVATE KEY-----".to_vec(),
        KEY[..40].to_vec(),
    ] {
        let error = validated_export(ExportBackupRequest {
            signing_key: key.clone(),
            ..request()
        })
        .unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        assert!(!error.message.contains("BEGIN"), "{}", error.message);
    }
}
