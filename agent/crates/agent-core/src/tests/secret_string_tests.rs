//! Tests for the `secret_string` module.
//!
//! The test that matters is the third one. A credential does not reach a log
//! line because somebody wrote `{secret:?}` on purpose; it reaches one because
//! a request struct that HOLDS it is `#[derive(Debug)]` and is logged whole.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{REDACTION, SecretString};

/// The shape a credential actually travels in: a field of a struct whose
/// `Debug` is derived, which is what a `tracing` macro prints.
#[derive(Debug)]
struct S3Credentials {
    access_key_id: String,
    secret_access_key: SecretString,
}

#[test]
fn debug_and_display_both_redact() {
    let secret = SecretString::new("AKIAIOSFODNN7EXAMPLE-secret".to_owned());

    assert_eq!(format!("{secret:?}"), REDACTION);
    assert_eq!(format!("{secret}"), REDACTION);
    assert!(!format!("{secret:?} {secret}").contains("EXAMPLE"));
}

#[test]
fn expose_returns_the_value() {
    let secret = SecretString::new("wJalrXUtnFEMI".to_owned());
    assert_eq!(secret.expose(), "wJalrXUtnFEMI");
}

#[test]
fn a_secret_inside_a_derived_struct_still_redacts() {
    let credentials = S3Credentials {
        access_key_id: "AKIAIOSFODNN7EXAMPLE".to_owned(),
        secret_access_key: SecretString::new("wJalrXUtnFEMI/K7MDENG/bPxRfiCY".to_owned()),
    };

    let rendered = format!("{credentials:?}");
    assert!(
        !rendered.contains("wJalrXUtnFEMI"),
        "the derived Debug of the holder leaked the secret: {rendered}"
    );
    assert!(
        rendered.contains(REDACTION),
        "the derived Debug must show the field as redacted: {rendered}"
    );
    // The non-secret field is still printed, so redaction is the field's doing
    // and not the whole struct having become opaque.
    assert!(rendered.contains("AKIAIOSFODNN7EXAMPLE"));
    assert_eq!(credentials.secret_access_key.expose().len(), 30);
    assert_eq!(credentials.access_key_id.len(), 20);
}
