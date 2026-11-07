//! What `restore_panel_self_signed` writes, in which order, and what it leaves
//! behind when nginx refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use crate::sites::SiteCertificate;
use crate::sites::fake_site_host::distro;
use crate::ssl::fake_ssl_host::{
    EXPIRY_UNIX, Event, FakeSslHost, PLACEHOLDER_CERTIFICATE_PEM, PLACEHOLDER_KEY_PEM,
    matching_material,
};
use crate::ssl::restore_panel_self_signed::restore_panel_self_signed;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_op_error::SslOpError;

fn host_name() -> Domain {
    Domain::parse_existing("panel.example.com").unwrap()
}

#[test]
fn a_generated_pair_replaces_the_panels_own_pair_and_returns_its_expiry() {
    let host = FakeSslHost::passing();
    let panel = SiteCertificate::for_panel();

    let expiry = restore_panel_self_signed(&host, distro(), &host_name()).unwrap();

    assert_eq!(expiry, EXPIRY_UNIX);
    assert_eq!(
        host.stored(panel.certificate_path()).unwrap(),
        PLACEHOLDER_CERTIFICATE_PEM
    );
    assert_eq!(host.stored(panel.key_path()).unwrap(), PLACEHOLDER_KEY_PEM);
    // No vhost is rewritten: the panel vhost already names these two files.
    assert_eq!(host.vhost_writes(), 0);
    assert!(host.has_marker(&self_signed_marker(&panel)));
}

#[test]
fn the_generated_certificate_is_requested_for_the_panels_host_name() {
    let host = FakeSslHost::passing();

    restore_panel_self_signed(&host, distro(), &host_name()).unwrap();

    let request = host
        .arguments()
        .into_iter()
        .find(|arguments| arguments.first().map(String::as_str) == Some("req"))
        .unwrap();
    assert_eq!(request[1], "/CN=panel.example.com/OU=maran-self-signed");
    assert_eq!(request[2], "subjectAltName=DNS:panel.example.com");
}

#[test]
fn the_marker_is_written_after_the_bytes_it_describes() {
    let host = FakeSslHost::passing();

    restore_panel_self_signed(&host, distro(), &host_name()).unwrap();

    assert_eq!(
        host.events(),
        vec![Event::MaterialWritten, Event::MarkerWritten]
    );
}

#[test]
fn an_nginx_refusal_restores_the_previous_pair_and_writes_no_marker() {
    let host = FakeSslHost::passing();
    let panel = SiteCertificate::for_panel();
    host.preinstall(&panel, &matching_material());
    host.reject_material_writes("unknown directive");

    let refused = restore_panel_self_signed(&host, distro(), &host_name());

    assert!(matches!(
        refused,
        Err(SslOpError::NginxValidation { ref stderr }) if stderr == "unknown directive"
    ));
    // The trusted pair is exactly what it was, and nothing claims it is a
    // placeholder.
    assert_eq!(
        host.stored(panel.certificate_path()).unwrap(),
        matching_material().certificate_pem()
    );
    assert_eq!(
        host.stored(panel.key_path()).unwrap(),
        matching_material().private_key_pem()
    );
    assert!(!host.has_marker(&self_signed_marker(&panel)));
    assert!(!host.events().contains(&Event::MarkerWritten));
}

#[test]
fn a_mismatched_generated_pair_is_refused_before_anything_is_written() {
    let host = FakeSslHost::passing().with_mismatched_key();

    let refused = restore_panel_self_signed(&host, distro(), &host_name());

    assert!(matches!(
        refused,
        Err(SslOpError::KeyDoesNotMatchCertificate)
    ));
    assert_eq!(host.stored_count(), 0);
    assert!(host.events().is_empty());
}

#[test]
fn regenerating_over_a_placeholder_replaces_it_and_keeps_it_marked() {
    let host = FakeSslHost::passing();
    let panel = SiteCertificate::for_panel();
    restore_panel_self_signed(&host, distro(), &host_name()).unwrap();

    let again = restore_panel_self_signed(
        &host,
        distro(),
        &Domain::parse_existing("other.example.com").unwrap(),
    );

    assert_eq!(again.unwrap(), EXPIRY_UNIX);
    assert!(host.has_marker(&self_signed_marker(&panel)));
    assert_eq!(
        host.events(),
        vec![
            Event::MaterialWritten,
            Event::MarkerWritten,
            Event::MaterialWritten,
            Event::MarkerWritten
        ]
    );
}

#[test]
fn a_trusted_certificate_is_replaced_because_the_operator_asked_for_it() {
    // The inverse of `generate_self_signed`, which never overwrites an
    // authority-signed certificate: this call is an administrator's explicit
    // request to leave it.
    let host = FakeSslHost::passing().with_authority_signed_certificate();
    let panel = SiteCertificate::for_panel();
    host.preinstall(&panel, &matching_material());

    restore_panel_self_signed(&host, distro(), &host_name()).unwrap();

    assert_eq!(
        host.stored(panel.certificate_path()).unwrap(),
        PLACEHOLDER_CERTIFICATE_PEM
    );
    assert!(host.has_marker(&self_signed_marker(&panel)));
}

#[test]
fn an_openssl_that_outruns_its_deadline_writes_nothing() {
    let host = FakeSslHost::passing();
    host.time_out_openssl();

    let refused = restore_panel_self_signed(&host, distro(), &host_name());

    assert!(matches!(refused, Err(SslOpError::Bound(_))));
    assert_eq!(host.stored_count(), 0);
}
