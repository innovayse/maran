//! Tests for the terminal answer `InstallCertificate` gives.
//!
//! Each of the three verdicts on the material must arrive as its own refusal,
//! because the panel tells the customer which half to fix from it; and every
//! other failure must stay an `AgentError`, or a fault of this machine would
//! read to the customer as a mistake in what they pasted.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::ssl::SslOpError;

use super::install_outcome;
use crate::proto::{CertificateMaterialRefusal, ErrorCode, install_certificate_response};

/// The refusal reason `outcome` is answered with, or a panic naming what it
/// was answered with instead.
fn refusal_of(outcome: Result<i64, SslOpError>) -> CertificateMaterialRefusal {
    match install_outcome(outcome) {
        install_certificate_response::Result::Refused(refused) => {
            CertificateMaterialRefusal::try_from(refused.reason).unwrap()
        }
        other => panic!("expected a refusal, got {other:?}"),
    }
}

#[test]
fn an_unreadable_certificate_is_refused_as_a_malformed_certificate() {
    let reason = refusal_of(Err(SslOpError::MalformedCertificate {
        reason: "unable to load certificate".to_owned(),
    }));

    assert_eq!(reason, CertificateMaterialRefusal::MalformedCertificate);
}

#[test]
fn an_unreadable_key_is_refused_as_a_malformed_private_key() {
    let reason = refusal_of(Err(SslOpError::MalformedPrivateKey));

    assert_eq!(reason, CertificateMaterialRefusal::MalformedPrivateKey);
}

#[test]
fn a_key_that_is_not_the_certificates_is_refused_as_a_mismatch() {
    let reason = refusal_of(Err(SslOpError::KeyDoesNotMatchCertificate));

    assert_eq!(
        reason,
        CertificateMaterialRefusal::KeyDoesNotMatchCertificate
    );
}

#[test]
fn an_installed_pair_answers_ok_with_the_expiry_the_operation_read() {
    let outcome = install_outcome(Ok(1_900_000_000));

    let install_certificate_response::Result::Ok(ok) = outcome else {
        panic!("expected ok, got {outcome:?}");
    };
    assert_eq!(ok.expires_at_unix, 1_900_000_000);
}

#[test]
fn a_failure_that_is_not_about_the_material_stays_an_agent_error() {
    // Inverse control: a refusal-for-everything mapping passes the three
    // tests above, and only this one notices.
    let outcome = install_outcome(Err(SslOpError::SiteNotFound {
        domain: "example.com".to_owned(),
    }));

    let install_certificate_response::Result::Error(error) = outcome else {
        panic!("expected an agent error, got {outcome:?}");
    };
    assert_eq!(error.code, ErrorCode::NotFound as i32);
}

#[test]
fn a_refused_vhost_stays_an_agent_error_carrying_the_validators_output() {
    let outcome = install_outcome(Err(SslOpError::NginxValidation {
        stderr: "nginx: [emerg] unexpected".to_owned(),
    }));

    let install_certificate_response::Result::Error(error) = outcome else {
        panic!("expected an agent error, got {outcome:?}");
    };
    assert_eq!(error.code, ErrorCode::ValidationFailed as i32);
}
