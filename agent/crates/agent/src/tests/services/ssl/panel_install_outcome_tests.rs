//! Tests for the terminal answer `InstallPanelCertificate` gives.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::ssl::SslOpError;

use super::panel_install_outcome;
use crate::proto::{CertificateMaterialRefusal, ErrorCode, install_panel_certificate_response};

#[test]
fn an_installed_pair_answers_its_expiry() {
    match panel_install_outcome(Ok(4_000_000_000)) {
        install_panel_certificate_response::Result::Ok(ok) => {
            assert_eq!(ok.expires_at_unix, 4_000_000_000);
        }
        other => panic!("expected ok, got {other:?}"),
    }
}

#[test]
fn a_key_that_is_not_the_certificates_is_its_own_refusal() {
    match panel_install_outcome(Err(SslOpError::KeyDoesNotMatchCertificate)) {
        install_panel_certificate_response::Result::Refused(refused) => assert_eq!(
            CertificateMaterialRefusal::try_from(refused.reason).unwrap(),
            CertificateMaterialRefusal::KeyDoesNotMatchCertificate
        ),
        other => panic!("expected a refusal, got {other:?}"),
    }
}

#[test]
fn a_refused_reload_stays_an_agent_error() {
    match panel_install_outcome(Err(SslOpError::ReloadFailed {
        stderr: "unit failed".to_owned(),
    })) {
        install_panel_certificate_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::SystemFailure as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
}
