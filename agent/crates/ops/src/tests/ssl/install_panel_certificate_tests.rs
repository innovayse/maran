//! What `install_panel_certificate` writes, and what it refuses to write.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::SiteCertificate;
use crate::sites::fake_site_host::distro;
use crate::ssl::fake_ssl_host::{EXPIRY_UNIX, FakeSslHost, matching_material, mismatched_material};
use crate::ssl::install_panel_certificate::install_panel_certificate;
use crate::ssl::ssl_op_error::SslOpError;

#[test]
fn a_matching_pair_replaces_the_panels_own_pair_and_returns_its_expiry() {
    let host = FakeSslHost::passing();
    let panel = SiteCertificate::for_panel();

    let expiry = install_panel_certificate(&host, distro(), &matching_material()).unwrap();

    assert_eq!(expiry, EXPIRY_UNIX);
    assert_eq!(
        panel.certificate_path().to_str(),
        Some("/etc/maran/tls/panel.crt")
    );
    assert_eq!(panel.key_path().to_str(), Some("/etc/maran/tls/panel.key"));
    assert_eq!(
        host.stored(panel.key_path()).unwrap(),
        matching_material().private_key_pem()
    );
    assert_eq!(
        host.stored(panel.certificate_path()).unwrap(),
        matching_material().certificate_pem()
    );
    // No vhost is rewritten: the panel vhost already names these two files.
    assert_eq!(host.vhost_writes(), 0);
}

#[test]
fn a_mismatched_pair_is_refused_before_anything_is_written() {
    let host = FakeSslHost::passing().with_mismatched_key();

    let refused = install_panel_certificate(&host, distro(), &mismatched_material());

    assert!(matches!(
        refused,
        Err(SslOpError::KeyDoesNotMatchCertificate)
    ));
    assert_eq!(host.stored_count(), 0);
}

#[test]
fn installing_the_same_pair_again_writes_nothing() {
    let host = FakeSslHost::passing();
    install_panel_certificate(&host, distro(), &matching_material()).unwrap();
    let protocols = host.protocols().len();

    install_panel_certificate(&host, distro(), &matching_material()).unwrap();

    assert_eq!(host.protocols().len(), protocols);
}
