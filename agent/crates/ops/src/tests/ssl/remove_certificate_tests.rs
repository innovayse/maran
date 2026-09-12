//! What `remove_certificate` takes away, and in which order.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::fake_site_host::{create_test_site, distro, php_input};
use crate::sites::{SiteCertificate, SitePaths, disable_site};
use crate::ssl::fake_ssl_host::{Event, FakeSslHost, matching_material};
use crate::ssl::install_certificate::install_certificate;
use crate::ssl::remove_certificate::remove_certificate;
use crate::ssl::ssl_op_error::SslOpError;

/// A host with `example.com` created and a certificate installed on it.
fn host_with_certificate() -> FakeSslHost {
    let host = FakeSslHost::passing();
    create_test_site(&host, &php_input()).unwrap();
    install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap();

    host
}

#[test]
fn the_material_is_removed_and_the_site_returns_to_plain_http() {
    let host = host_with_certificate();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);

    remove_certificate(&host, distro(), &input).unwrap();

    assert_eq!(host.stored_count(), 0);
    let vhost = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    assert!(!vhost.contains(&certificate.certificate_path().display().to_string()));
}

#[test]
fn the_vhost_stops_pointing_at_the_material_before_the_material_is_deleted() {
    let host = host_with_certificate();

    remove_certificate(&host, distro(), &php_input()).unwrap();

    // The other order leaves the running configuration naming an
    // `ssl_certificate` that is not there, so the next `nginx -t` fails — this
    // one's, or an unrelated site's minutes later — and nginx does not start
    // again after the next reboot.
    let events = host.events();
    let vhost_rewired = events
        .iter()
        .rposition(|event| *event == Event::VhostWritten)
        .unwrap();
    let material_removed = events
        .iter()
        .position(|event| *event == Event::MaterialRemoved)
        .unwrap();
    assert!(vhost_rewired < material_removed);
}

#[test]
fn removing_when_nothing_is_installed_is_reported_as_not_found() {
    let host = FakeSslHost::passing();
    create_test_site(&host, &php_input()).unwrap();

    let failure = remove_certificate(&host, distro(), &php_input()).unwrap_err();

    // The contract's own answer (`ssl.proto`), and not a success: the panel
    // distinguishes "there was nothing to remove" from "it is gone now".
    assert!(matches!(failure, SslOpError::NotFound { .. }));
}

#[test]
fn a_lone_private_key_left_by_an_interrupted_install_is_still_removed() {
    let host = FakeSslHost::passing();
    create_test_site(&host, &php_input()).unwrap();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);
    host.preinstall(&certificate, &matching_material());
    // As if the process died between the two writes, the wrong way round.
    host.forget(certificate.certificate_path());

    remove_certificate(&host, distro(), &input).unwrap();

    // A removal that only looked at the certificate would leave the secret half
    // behind for good — no operation would ever see it again.
    assert_eq!(host.stored_count(), 0);
}

#[test]
fn a_removal_for_a_site_that_does_not_exist_is_refused() {
    let host = FakeSslHost::passing();
    let input = php_input();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );

    let failure = remove_certificate(&host, distro(), &input).unwrap_err();

    assert!(matches!(failure, SslOpError::SiteNotFound { .. }));
}

#[test]
fn removing_a_certificate_from_a_suspended_site_takes_the_material_and_leaves_the_stub() {
    // The stub names no certificate, so there is nothing to rewire — and
    // rendering the site's own plain-HTTP vhost over it would un-suspend the
    // site on the strength of a certificate removal.
    let host = FakeSslHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let certificate = SiteCertificate::for_domain(&input.domain);
    host.preinstall(&certificate, &matching_material());
    disable_site(&host, distro(), &input).unwrap();
    let suspended = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    let writes_before = host.vhost_writes();

    remove_certificate(&host, distro(), &input).unwrap();

    assert_eq!(host.stored(certificate.certificate_path()), None);
    assert_eq!(host.stored(certificate.key_path()), None);
    assert_eq!(
        host.config(&SitePaths::for_site(&input.account, &input.domain).config_path)
            .unwrap(),
        suspended,
        "a certificate removal must not un-suspend a site"
    );
    assert_eq!(host.vhost_writes(), writes_before);
}
