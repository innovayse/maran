//! What a `SourceCredential` must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::proto::ErrorCode;

/// An inert key document: the OpenSSH header, a body that starts with the format's
/// magic and a footer. It opens nothing anywhere.
const KEY: &str = "-----BEGIN OPENSSH PRIVATE KEY-----\n\
    b3BlbnNzaC1rZXktdjEACQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ\n\
    -----END OPENSSH PRIVATE KEY-----\n";

/// An inert host key: an ed25519 blob of the right shape.
const HOST_KEY: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

/// A marker planted in a private key that must never be echoed back.
const CANARY: &str = "CANARY-private-8c41";

fn credential(private_key: &str, host_key: &str) -> Option<proto::SourceCredential> {
    Some(proto::SourceCredential {
        private_key: private_key.as_bytes().to_vec(),
        host_key: host_key.to_owned(),
    })
}

#[test]
fn a_well_formed_credential_yields_the_key_and_the_pinned_host_key() {
    let accepted = validated_source_credential(credential(KEY, HOST_KEY)).unwrap();

    assert_eq!(accepted.private_key.expose(), KEY);
    assert_eq!(accepted.host_key.as_str(), HOST_KEY);
    assert_eq!(accepted.host_key.algorithm(), "ssh-ed25519");
}

#[test]
fn an_absent_credential_is_invalid_input() {
    let error = validated_source_credential(None).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_private_key_that_is_not_an_openssh_document_is_invalid_input_and_never_echoed() {
    for key in [
        String::new(),
        CANARY.to_owned(),
        format!(
            "-----BEGIN OPENSSH PRIVATE KEY-----\n{CANARY}\n-----END OPENSSH PRIVATE KEY-----\n"
        ),
        format!("{KEY}\u{7}"),
        KEY.replace("BEGIN", "BEGINN"),
    ] {
        let error = validated_source_credential(credential(&key, HOST_KEY)).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{key:?}");
        assert!(!error.message.contains(CANARY), "{}", error.message);
    }
}

#[test]
fn a_host_key_that_is_not_one_algorithm_and_one_blob_is_invalid_input() {
    for host_key in [
        "",
        "ssh-ed25519",
        "ssh-dss AAAA",
        "ssh-ed25519 not-base64!",
        "ssh-ed25519 AAAA\nssh-rsa AAAA",
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH extra",
    ] {
        let error = validated_source_credential(credential(KEY, host_key)).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{host_key:?}");
    }
}
