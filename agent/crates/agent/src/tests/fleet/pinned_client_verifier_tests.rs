//! Tests for the verifier that admits only the pinned panel certificate.
//!
//! The verifier is driven directly with a fixed instant, so no case depends on today's date.
//! Every refusal has a control: the accepting case beside it uses the same helpers, so a
//! verifier that refused everything would fail the control and not just pass the refusals.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::time::Duration;

use rustls::pki_types::{CertificateDer, UnixTime};
use rustls::server::danger::ClientCertVerifier;
use rustls::{CertificateError, Error};

use super::PinnedClientVerifier;
use crate::fleet::fleet_crypto_provider::fleet_crypto_provider;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use crate::fleet::test_certificates::{
    TEST_NOW_SECONDS, TestCertificate, expired_client_certificate, valid_client_certificate,
    valid_server_certificate,
};

/// The instant every case is judged at.
fn now() -> UnixTime {
    UnixTime::since_unix_epoch(Duration::from_secs(TEST_NOW_SECONDS))
}

/// A verifier whose slot pins `certificate`, and the slot, so a case can change the pin.
fn verifier_pinning(certificate: &TestCertificate) -> (PinnedClientVerifier, Arc<PinSlot>) {
    let pin = PinnedPanelCertificate::from_der(certificate.der.clone()).unwrap();
    let slot = Arc::new(PinSlot::holding(pin));
    let verifier = PinnedClientVerifier::new(slot.clone(), fleet_crypto_provider());
    (verifier, slot)
}

/// What the verifier answers when `presented` arrives alone, with no intermediates.
fn verdict(verifier: &PinnedClientVerifier, presented: &TestCertificate) -> Result<(), Error> {
    verifier
        .verify_client_cert(&CertificateDer::from(presented.der.clone()), &[], now())
        .map(|_| ())
}

#[test]
fn the_pinned_certificate_is_admitted() {
    let panel = valid_client_certificate("panel");
    let (verifier, _slot) = verifier_pinning(&panel);

    assert!(verdict(&verifier, &panel).is_ok());
}

#[test]
fn a_different_key_with_the_same_subject_is_refused() {
    let panel = valid_client_certificate("panel");
    let impostor = valid_client_certificate("panel");
    let (verifier, _slot) = verifier_pinning(&panel);

    assert!(verdict(&verifier, &impostor).is_err());
    // The control: the pinned certificate itself still passes, so the refusal above is about
    // the impostor and not about this verifier refusing everything.
    assert!(verdict(&verifier, &panel).is_ok());
}

#[test]
fn the_same_key_certified_again_is_refused_although_the_chain_check_accepts_it() {
    let panel = valid_client_certificate("panel");
    let reissued = panel.reissued();
    let (verifier, _slot) = verifier_pinning(&panel);

    // The witness that the fingerprint comparison is what refuses: the chain check over the
    // pin as the only anchor compares subject and public key, and both are the same.
    let pin = PinnedPanelCertificate::from_der(panel.der.clone()).unwrap();
    let chain_only = pin.chain_verifier().verify_client_cert(
        &CertificateDer::from(reissued.der.clone()),
        &[],
        now(),
    );
    assert!(
        chain_only.is_ok(),
        "the chain check alone must accept the re-issued certificate, or this test proves nothing"
    );
    assert_ne!(reissued.der, panel.der);

    assert!(verdict(&verifier, &reissued).is_err());
}

#[test]
fn an_expired_pinned_certificate_is_refused_for_being_expired() {
    let panel = expired_client_certificate("panel");
    let (verifier, _slot) = verifier_pinning(&panel);

    let error = verdict(&verifier, &panel).unwrap_err();

    assert!(
        matches!(
            error,
            Error::InvalidCertificate(
                CertificateError::Expired | CertificateError::ExpiredContext { .. }
            )
        ),
        "expected an expiry refusal, got {error:?}"
    );
}

#[test]
fn a_pinned_certificate_not_marked_for_client_use_is_refused() {
    let node_like = valid_server_certificate();
    let (verifier, _slot) = verifier_pinning(&node_like);

    assert!(verdict(&verifier, &node_like).is_err());
}

#[test]
fn nothing_pinned_admits_nobody() {
    let panel = valid_client_certificate("panel");
    let verifier = PinnedClientVerifier::new(Arc::new(PinSlot::empty()), fleet_crypto_provider());

    assert!(verdict(&verifier, &panel).is_err());
}

#[test]
fn removing_the_pin_stops_admission_without_rebuilding_the_verifier() {
    let panel = valid_client_certificate("panel");
    let (verifier, slot) = verifier_pinning(&panel);
    assert!(
        verdict(&verifier, &panel).is_ok(),
        "control: admitted while pinned"
    );

    slot.replace(None);

    assert!(verdict(&verifier, &panel).is_err());
}

#[test]
fn replacing_the_pin_admits_the_new_panel_and_refuses_the_old_one() {
    let old_panel = valid_client_certificate("panel");
    let new_panel = valid_client_certificate("panel");
    let (verifier, slot) = verifier_pinning(&old_panel);

    slot.replace(Some(
        PinnedPanelCertificate::from_der(new_panel.der.clone()).unwrap(),
    ));

    assert!(verdict(&verifier, &new_panel).is_ok());
    assert!(verdict(&verifier, &old_panel).is_err());
}

#[test]
fn a_client_that_sends_intermediates_is_refused() {
    let panel = valid_client_certificate("panel");
    let other = valid_client_certificate("other");
    let (verifier, _slot) = verifier_pinning(&panel);

    let with_intermediate = verifier.verify_client_cert(
        &CertificateDer::from(panel.der.clone()),
        &[CertificateDer::from(other.der.clone())],
        now(),
    );

    assert!(with_intermediate.is_err());
    assert!(
        verdict(&verifier, &panel).is_ok(),
        "control: alone it is admitted"
    );
}

#[test]
fn no_certificate_authority_hint_is_sent_and_signatures_can_be_verified() {
    let panel = valid_client_certificate("panel");
    let (verifier, _slot) = verifier_pinning(&panel);

    assert!(verifier.root_hint_subjects().is_empty());
    assert!(!verifier.supported_verify_schemes().is_empty());
    assert!(verifier.client_auth_mandatory());
}

#[test]
fn a_pin_that_is_not_a_certificate_cannot_be_built() {
    let garbage = PinnedPanelCertificate::from_der(vec![0x30, 0x03, 0x01, 0x01, 0x00]);

    assert!(garbage.is_err());
}
