//! Tests for [`delete_site_with_certificate`].
//!
//! The pair this exists to make inseparable: the vhost and the private key.
//! Each half already had its own passing tests while the two were never called
//! together anywhere in the agent, which is exactly how a deleted site's
//! `privkey.pem` came to survive on a real host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::php::fake_php_host::FakePhpHost;
use crate::sites::SiteCertificate;
use crate::sites::fake_site_host::{distro, php_identity, php_input};
use crate::ssl::delete_site_with_certificate::delete_site_with_certificate;
use crate::ssl::fake_ssl_host::{Event, FakeSslHost, matching_material};

/// The vhost every test here acts on.
const VHOST: &str = "/etc/maran/nginx/sites/example.com.conf";

#[test]
fn deleting_a_site_takes_its_private_key_with_it() {
    let host = FakeSslHost::passing();
    let input = php_input();
    crate::sites::create_site(
        &host,
        &FakePhpHost::with_installed(&["8.3", "8.4"]),
        distro(),
        &input,
        4,
        &[],
    )
    .unwrap();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );

    delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    // Both halves, and the second is the one that had no caller. A key that
    // outlives its site is a live secret nothing accounts for, and the site
    // created tomorrow on this domain may belong to a different account — which
    // would then be served the previous tenant's certificate.
    assert!(host.config(Path::new(VHOST)).is_none());
    assert_eq!(host.stored_count(), 0);
    assert!(host.events().contains(&Event::MaterialRemoved));
}

#[test]
fn a_site_that_never_had_a_certificate_is_deleted_without_complaint() {
    // The inverse control. A pair that only ever runs against a site WITH
    // material would pass just as happily if the purge refused everything, and
    // most sites have no certificate at all.
    let host = FakeSslHost::passing();
    let input = php_input();
    crate::sites::create_site(
        &host,
        &FakePhpHost::with_installed(&["8.3", "8.4"]),
        distro(),
        &input,
        4,
        &[],
    )
    .unwrap();

    delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    assert!(host.config(Path::new(VHOST)).is_none());
    assert_eq!(host.stored_count(), 0);
}

#[test]
fn a_site_that_is_already_gone_is_refused_and_no_key_is_touched() {
    // Ordering, stated as a behaviour: the purge is reached only when the vhost
    // removal succeeded. A refused deletion that had nevertheless unlinked the
    // key would leave a site still being SERVED whose certificate had gone,
    // which nginx answers by refusing to reload at all.
    let host = FakeSslHost::passing();
    host.preinstall(
        &SiteCertificate::for_domain(&php_input().domain),
        &matching_material(),
    );

    let outcome = delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    );

    assert!(outcome.is_err());
    assert_eq!(host.stored_count(), 2);
}
