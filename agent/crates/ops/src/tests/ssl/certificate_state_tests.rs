//! What is installed for a domain, decided from files and never from text.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use crate::sites::SiteCertificate;
use crate::ssl::certificate_state::certificate_state;
use crate::ssl::fake_ssl_host::{FakeSslHost, matching_material};
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_op_error::SslOpError;

/// The domain every test here asks about.
fn domain() -> Domain {
    Domain::parse("ftp.example.test").unwrap()
}

/// Its material, in the agent's own store.
fn certificate() -> SiteCertificate {
    SiteCertificate::for_domain(&domain())
}

#[test]
fn material_with_a_marker_beside_it_is_reported_as_a_placeholder() {
    let host = FakeSslHost::passing();
    host.preinstall(&certificate(), &matching_material());
    host.premark(&self_signed_marker(&certificate()));

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(state.present);
    assert!(state.is_self_signed_placeholder);
}

#[test]
fn material_without_a_marker_is_never_reported_as_a_placeholder_however_its_subject_reads() {
    // The regression this function must not reintroduce, as the reviewer wrote
    // it: a certificate that is self-signed AND whose subject contains the
    // marker text, in the exact shape openssl prints when a value carries a
    // comma — the whole value QUOTED, not escaped. A splitter without quoting
    // cuts ` OU = maran-self-signed` out of the middle of that organisation and
    // trims it to an exact match. Without the FILE this is somebody's real
    // certificate, and the old design destroyed it and its private key.
    let host = FakeSslHost::passing().with_self_signed_subject(
        "O = \"Example, OU = maran-self-signed, more\", CN = ftp.example.test",
    );
    host.preinstall(&certificate(), &matching_material());

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(state.present);
    assert!(!state.is_self_signed_placeholder);
    // And the subject above was never even fetched: no program was run at all,
    // which is the property that makes the quoting rule above irrelevant rather
    // than merely handled. Asserted as the whole list, so a run that grew an
    // argv cannot pass.
    assert_eq!(host.arguments(), Vec::<Vec<String>>::new());
}

#[test]
fn a_domain_with_no_material_is_absent_rather_than_an_error() {
    let host = FakeSslHost::passing();

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(!state.present);
    assert!(!state.is_self_signed_placeholder);
    // The paths are still returned, and asserted whole rather than by their
    // ending: the caller's refusal names where the material would have to be,
    // and a refusal naming the wrong directory sends an operator to the wrong
    // machine.
    assert_eq!(
        state.certificate_path,
        "/etc/maran/certificates/ftp.example.test/fullchain.pem"
    );
    assert_eq!(
        state.private_key_path,
        "/etc/maran/certificates/ftp.example.test/privkey.pem"
    );
}

#[test]
fn a_marker_left_behind_with_no_material_describes_nothing() {
    // The state an interrupted removal leaves: the marker outlived the bytes it
    // was written about. There is no material, so there is nothing for it to be
    // a placeholder OF, and saying otherwise would describe files that are not
    // there.
    let host = FakeSslHost::passing();
    host.premark(&self_signed_marker(&certificate()));

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(!state.present);
    assert!(!state.is_self_signed_placeholder);
}

#[test]
fn a_certificate_whose_private_key_is_missing_is_not_reported_as_present() {
    // Every caller of this type points a TLS daemon at BOTH paths, and a daemon
    // handed a certificate with no key refuses to start. Reporting "present"
    // here would be reporting material no daemon can serve.
    let host = FakeSslHost::passing();
    host.preinstall(&certificate(), &matching_material());
    host.forget(certificate().key_path());

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(!state.present);
    assert!(!state.is_self_signed_placeholder);
}

#[test]
fn a_marker_holding_content_the_agent_never_wrote_is_still_the_agents_own_fact() {
    // The marker that is "not ours to trust" in the only sense the store allows
    // one to be: a file in a 0700 directory only root writes, holding something
    // the agent's own writer never puts there. Its EXISTENCE is the fact and its
    // content is never read — reading it back to decide whether the marker is
    // really ours would rebuild, one directory further in, the text-parsing
    // decision this whole design exists to abolish.
    let host = FakeSslHost::passing();
    host.preinstall(&certificate(), &matching_material());
    host.plant(
        &self_signed_marker(&certificate()),
        "planted by something that is not this agent\n",
    );

    let state = certificate_state(&host, &domain()).unwrap();

    assert!(state.present);
    assert!(state.is_self_signed_placeholder);
}

#[test]
fn a_marker_that_cannot_be_read_is_an_error_and_never_a_confident_no() {
    // The other reading of "not ours to trust": the agent cannot tell whose
    // material this is. Flattening that into `false` would hand a caller a
    // confident answer manufactured by an I/O error — which is how the
    // destroyed key happened, by a different route.
    let host = FakeSslHost::passing();
    host.preinstall(&certificate(), &matching_material());
    host.fail_read(&self_signed_marker(&certificate()));

    let failure = certificate_state(&host, &domain()).unwrap_err();

    assert!(matches!(failure, SslOpError::MaterialWrite { .. }));
}

#[test]
fn a_certificate_that_cannot_be_read_is_an_error_and_never_absent() {
    // A file that is there and unreadable is not an empty directory. Reporting
    // it as absent would tell a caller to install material over material.
    let host = FakeSslHost::passing();
    host.preinstall(&certificate(), &matching_material());
    host.fail_read(certificate().certificate_path());

    let failure = certificate_state(&host, &domain()).unwrap_err();

    assert!(matches!(failure, SslOpError::MaterialWrite { .. }));
}
