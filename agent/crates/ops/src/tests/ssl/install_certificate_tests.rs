//! What `install_certificate` decides, and what it refuses to do first.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::domain::Domain;

use crate::sites::fake_site_host::{create_test_site, distro, php_input};
use crate::sites::{SiteCertificate, SitePaths};
use crate::ssl::fake_ssl_host::{
    EXPIRY_UNIX, Event, FakeSslHost, KEY_PEM, matching_material, mismatched_material,
};
use crate::ssl::install_certificate::install_certificate;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_op_error::SslOpError;

/// A host with `example.com` already created, so a certificate has a site to be
/// installed into.
fn host_with_site() -> FakeSslHost {
    let host = FakeSslHost::passing();
    create_test_site(&host, &php_input()).unwrap();

    host
}

#[test]
fn a_matching_pair_is_installed_and_the_certificates_own_expiry_returned() {
    let host = host_with_site();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);

    let expiry = install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    // The number the panel schedules renewal from, read out of the certificate
    // rather than out of anything the caller said about it.
    assert_eq!(expiry, EXPIRY_UNIX);
    assert_eq!(
        host.stored(certificate.key_path()).unwrap(),
        matching_material().private_key_pem()
    );
    assert_eq!(
        host.stored(certificate.certificate_path()).unwrap(),
        matching_material().certificate_pem()
    );

    let vhost = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    assert!(vhost.contains(&certificate.certificate_path().display().to_string()));
    assert!(vhost.contains(&certificate.key_path().display().to_string()));
}

#[test]
fn the_material_is_written_before_the_vhost_points_at_it() {
    let host = host_with_site();

    install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap();

    // The site's own creation wrote the first vhost; what matters is that the
    // material exists before the vhost that names it. The other order is a
    // configuration referring to a file that is not there, which is what stops
    // nginx from starting after the next reboot.
    assert_eq!(
        host.events(),
        vec![
            Event::VhostWritten,
            Event::MaterialWritten,
            Event::VhostWritten
        ]
    );
}

#[test]
fn a_mismatched_pair_is_refused_before_anything_is_written() {
    let host = host_with_site();
    let host = host.with_mismatched_key();
    let input = php_input();
    let before = host.vhost_writes();

    let failure = install_certificate(&host, distro(), &input, &mismatched_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::KeyDoesNotMatchCertificate));
    // Nothing at all: a mismatched pair passes `nginx -t` and fails at the
    // first handshake, so the refusal has to happen before the swap, not after
    // it.
    assert_eq!(host.stored_count(), 0);
    assert_eq!(host.vhost_writes(), before);
}

#[test]
fn installing_byte_identical_material_twice_writes_nothing_the_second_time() {
    let host = host_with_site();
    let input = php_input();

    let first = install_certificate(&host, distro(), &input, &matching_material()).unwrap();
    let events = host.events();
    let second = install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    // The contract's own words (`ssl.proto`): a second installation of
    // identical material is a no-op success. The panel retries after a timeout,
    // and a key rewritten and an nginx reloaded per retry is a storm.
    assert_eq!(first, second);
    assert_eq!(host.events(), events);
}

#[test]
fn different_material_for_the_same_domain_replaces_it() {
    let host = host_with_site();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);
    host.preinstall(&certificate, &mismatched_material());

    install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    assert_eq!(
        host.stored(certificate.key_path()).unwrap(),
        matching_material().private_key_pem()
    );
}

#[test]
fn a_vhost_nginx_rejects_leaves_the_previous_one_serving() {
    let host = host_with_site();
    let input = php_input();
    let plain = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    host.reject_validation("ssl_certificate directive is not allowed here");

    let failure = install_certificate(&host, distro(), &input, &matching_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::NginxValidation { .. }));
    // The site is still the working plain-HTTP site it was. This is why the
    // material and the vhost are two writes: one rollback restores a vhost that
    // serves traffic, rather than leaving a TLS vhost nginx refused.
    assert_eq!(
        host.config(&SitePaths::for_site(&input.account, &input.domain).config_path)
            .unwrap(),
        plain
    );
}

#[test]
fn a_certificate_for_a_site_that_does_not_exist_is_refused() {
    let host = FakeSslHost::passing();

    let failure =
        install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::SiteNotFound { .. }));
    // No orphaned key: material written for a site that does not exist is a
    // secret on disk that nothing serves and no operation removes.
    assert_eq!(host.stored_count(), 0);
}

#[test]
fn the_private_key_never_reaches_a_command_argument() {
    let host = host_with_site();

    install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap();

    for argv in host.arguments() {
        for argument in argv {
            assert!(
                !argument.contains("PRIVATE KEY"),
                "an argv is world-readable through /proc/<pid>/cmdline: {argument}"
            );
        }
    }
    assert!(!KEY_PEM.is_empty());
}

#[test]
fn a_key_openssl_cannot_read_is_reported_with_no_detail_at_all() {
    let host = host_with_site();
    // openssl echoing the key it choked on is the exact leak the empty variant
    // exists to make impossible.
    host.refuse("pkey", KEY_PEM);

    let failure =
        install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::MalformedPrivateKey));
    assert!(!failure.to_string().contains("PRIVATE KEY"));
}

#[test]
fn a_certificate_openssl_cannot_read_is_reported_with_the_tools_own_words() {
    let host = host_with_site();
    host.refuse("x509", "unable to load certificate");

    let failure =
        install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap_err();

    match failure {
        SslOpError::MalformedCertificate { reason } => {
            assert_eq!(reason, "unable to load certificate");
        }
        other => panic!("expected a malformed certificate, got {other:?}"),
    }
}

#[test]
fn a_reinstall_of_the_same_material_does_not_spawn_the_matching_check_again() {
    let host = host_with_site();
    install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap();
    let after_first = host.arguments().len();

    install_certificate(&host, distro(), &php_input(), &matching_material()).unwrap();

    // Only the `-enddate` call, because the answer to "does this key match this
    // certificate?" is the one this operation already established when it
    // installed exactly these bytes.
    let spawned = host.arguments().len() - after_first;
    assert_eq!(spawned, 1, "{:?}", host.arguments());
}

#[test]
fn an_alias_of_the_site_is_served_by_the_same_certificate() {
    let host = host_with_site();
    let input = php_input();

    install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    let vhost = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    let alias = Domain::parse("www.example.com").unwrap();
    // Rendered by the site area's own renderer, so the TLS half serves exactly
    // what the plain half serves — including every alias.
    assert!(vhost.contains(alias.as_str()));
}

#[test]
fn a_marker_removal_that_failed_once_is_retried_by_the_next_install() {
    let host = host_with_site();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);
    // As if a placeholder had been generated here earlier.
    host.premark(&self_signed_marker(&certificate));
    host.fail_marker_removal_once();

    // The customer's real certificate arrives, the material is written, and the
    // marker removal hits a full disk.
    let refused = install_certificate(&host, distro(), &input, &matching_material());
    assert!(matches!(refused, Err(SslOpError::MaterialWrite { .. })));
    assert!(host.has_marker(&self_signed_marker(&certificate)));

    // The panel retries with the same material. The removal is attempted OUTSIDE
    // the "did we write anything?" guard, so this converges instead of skipping
    // the block forever: a marker stuck beside a real certificate is a licence
    // for the next `GenerateSelfSigned` to destroy it, and `subject == issuer`
    // only rescues a certificate that happens to be CA-signed.
    install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    assert!(!host.has_marker(&self_signed_marker(&certificate)));
}
