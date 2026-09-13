//! Tests for [`enable_site`].
//!
//! Enabling is the operation a retry is most likely to find already done, so
//! what these tests pin is that the second call is silent: no re-render, no
//! `nginx -t`, no reload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::sites::fake_site_host::{FakeSiteHost, create_test_site, distro, php_input};
use crate::sites::{SitesOpError, disable_site, enable_site};

#[test]
fn enabling_a_site_that_is_already_enabled_changes_nothing() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    enable_site(&host, distro(), &input).unwrap();

    // No second write, therefore no second `nginx -t` and no second reload:
    // the panel retries after a timeout, and a reload per retry is a storm.
    assert_eq!(host.writes(), 1);
}

#[test]
fn enabling_a_suspended_site_restores_its_own_configuration() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let original = host
        .config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
        .unwrap();
    disable_site(&host, distro(), &input).unwrap();

    enable_site(&host, distro(), &input).unwrap();

    assert_eq!(
        host.config(Path::new("/etc/maran/nginx/sites/example.com.conf")),
        Some(original)
    );
}

#[test]
fn enabling_a_site_that_does_not_exist_is_not_found() {
    let host = FakeSiteHost::passing();

    match enable_site(&host, distro(), &php_input()) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
}
