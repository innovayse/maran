//! Tests for [`disable_site`].
//!
//! The whole point of disabling rather than deleting is that the site keeps
//! answering the one request it must keep answering, so that is what these
//! tests pin: `sites.proto` keeps the vhost on disable "so SSL renewal and SEO
//! are not disrupted", and a suspended site that stops serving
//! `/.well-known/acme-challenge/` cannot renew — it comes back from suspension
//! with an expired certificate, weeks later, with nothing in a log to say why.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::sites::{SitesOpError, disable_site};

use crate::sites::fake_site_host::{FakeSiteHost, create_test_site, distro, php_input};

/// The vhost every test here acts on.
const VHOST: &str = "/etc/maran/nginx/sites/example.com.conf";

#[test]
fn disabling_replaces_the_vhost_and_keeps_the_acme_location() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    disable_site(&host, distro(), &input).unwrap();

    let vhost = host.config(Path::new(VHOST)).unwrap();
    assert!(
        vhost.contains("location ^~ /.well-known/acme-challenge/ {"),
        "a suspended site must still answer the ACME challenge: {vhost}"
    );
    assert!(
        vhost.contains("root /srv/homes/acme/sites/example.com;"),
        "the challenge is answered from the document root, so the root must survive: {vhost}"
    );
    assert!(vhost.contains("return 403 \"This site has been suspended.\";"));
    // Nothing of the account's own is reachable any more.
    assert!(!vhost.contains("fastcgi_pass"));
}

#[test]
fn disabling_a_site_that_is_already_disabled_changes_nothing() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    disable_site(&host, distro(), &input).unwrap();
    let suspended = host.config(Path::new(VHOST));

    disable_site(&host, distro(), &input).unwrap();

    // Two writes in total — the create and the first disable. The retry the
    // panel makes after a timeout must not become a second `nginx -t` and a
    // second reload of an unchanged file.
    assert_eq!(host.writes(), 2);
    assert_eq!(host.config(Path::new(VHOST)), suspended);
}

#[test]
fn disabling_a_site_that_does_not_exist_is_not_found() {
    let host = FakeSiteHost::passing();

    match disable_site(&host, distro(), &php_input()) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
}

#[test]
fn a_rejected_suspension_leaves_the_live_vhost_in_place() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let live = host.config(Path::new(VHOST));
    host.reject_validation("nginx: [emerg] unexpected end of file");

    match disable_site(&host, distro(), &input) {
        Err(SitesOpError::NginxValidation { stderr }) => {
            assert!(stderr.contains("unexpected end of file"), "got {stderr}");
        }
        other => panic!("expected NginxValidation, got {other:?}"),
    }
    // The protocol restores the previous content, so a refused suspension
    // leaves the site serving rather than serving nothing.
    assert_eq!(host.config(Path::new(VHOST)), live);
}
