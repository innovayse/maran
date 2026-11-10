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
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{distro, php_identity, php_input};
use crate::sites::fake_site_log_host::FakeSiteLogHost;
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
        &FakeSiteLogHost::new(),
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
        &FakeSiteLogHost::new(),
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
        &FakeSiteLogHost::new(),
        distro(),
        &php_identity(),
        None,
    );

    assert!(outcome.is_err());
    assert_eq!(host.stored_count(), 2);
}

/// Creates the php site every log test deletes, with its logs on the fake log host.
fn site_with_logs() -> (FakeSslHost, FakeSiteLogHost) {
    let host = FakeSslHost::passing();
    crate::sites::create_site(
        &host,
        &FakePhpHost::with_installed(&["8.3", "8.4"]),
        distro(),
        &php_input(),
        4,
        &[],
    )
    .unwrap();
    let logs = FakeSiteLogHost::new();
    logs.put("example.com.access.log", b"203.0.113.9 GET /?sid=1");
    logs.put("example.com.error.log", b"error");
    logs.put("example.com.access.log.2.gz", b"old");
    logs.put("other.org.access.log", b"another site");

    (host, logs)
}

#[test]
fn deleting_a_site_takes_its_visitor_logs_and_leaves_every_other_sites() {
    let (host, logs) = site_with_logs();

    delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        &logs,
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    assert_eq!(logs.names(), vec!["other.org.access.log"]);
}

#[test]
fn a_refused_deletion_leaves_the_logs_of_a_site_that_is_still_served() {
    // Ordering as behaviour, the inverse of the case above: logs are removed only
    // after the vhost is gone, or the site would be left serving into a missing file.
    let (host, logs) = site_with_logs();
    host.reject_validation("nginx: configuration file test failed");

    let outcome = delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        &logs,
        distro(),
        &php_identity(),
        None,
    );

    assert!(outcome.is_err());
    assert!(logs.names().contains(&"example.com.access.log".to_owned()));
}

#[test]
fn the_retry_of_a_deletion_that_found_the_vhost_gone_still_removes_the_logs() {
    let (host, logs) = site_with_logs();
    delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        &FakeSiteLogHost::new(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();
    assert_eq!(
        logs.names().len(),
        4,
        "the first run was handed an empty log host"
    );

    let retry = delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        &logs,
        distro(),
        &php_identity(),
        None,
    );

    assert!(matches!(retry, Err(SitesOpError::NotFound { .. })));
    assert_eq!(logs.names(), vec!["other.org.access.log"]);
}

#[test]
fn logs_that_cannot_be_removed_are_reported_and_not_swallowed() {
    let (host, logs) = site_with_logs();
    logs.refuse_the_directory();

    let outcome = delete_site_with_certificate(
        &host,
        &FakePhpHost::empty(),
        &logs,
        distro(),
        &php_identity(),
        None,
    );

    assert!(matches!(outcome, Err(SitesOpError::LogUnreadable { .. })));
}
