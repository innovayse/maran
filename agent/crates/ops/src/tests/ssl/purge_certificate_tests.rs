//! Taking a deleted site's material with it, including when that fails.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::SiteCertificate;
use crate::sites::fake_site_host::{distro, php_input};
use crate::ssl::fake_ssl_host::{Event, FakeSslHost, matching_material};
use crate::ssl::purge_certificate::purge_certificate;

#[test]
fn a_deleted_sites_material_is_removed() {
    let host = FakeSslHost::passing();
    let input = php_input();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );

    purge_certificate(&host, distro(), &input.domain);

    // A key that outlives its site is a live secret nothing accounts for — and
    // the site created tomorrow on this domain may belong to a different
    // account, which would then serve the previous tenant's certificate.
    assert_eq!(host.stored_count(), 0);
    assert_eq!(host.events(), vec![Event::MaterialRemoved]);
}

#[test]
fn purging_a_site_that_never_had_a_certificate_does_nothing_and_says_nothing() {
    let host = FakeSslHost::passing();

    purge_certificate(&host, distro(), &php_input().domain);

    assert_eq!(host.stored_count(), 0);
}

#[test]
fn a_removal_that_fails_does_not_stop_the_caller() {
    let host = FakeSslHost::passing();
    let input = php_input();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );
    host.fail_material_removal();

    // Returns, rather than propagating: the site whose vhost is already gone IS
    // deleted, and failing that operation would have the caller retry something
    // that has already succeeded. The operator learns about the leftover key
    // from the `warn` this emits, which is the only reporting channel a
    // best-effort cleanup has.
    purge_certificate(&host, distro(), &input.domain);

    // The material is still there, which is exactly what the warning says.
    assert_eq!(host.stored_count(), 2);
}
