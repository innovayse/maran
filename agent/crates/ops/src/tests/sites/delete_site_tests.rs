//! Tests for [`delete_site`].
//!
//! Deleting is the operation whose retry is most likely to arrive after it has
//! already succeeded — the panel times out, requeues, and asks again — so the
//! second call's answer is the thing worth pinning.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::sites::{SitesOpError, delete_site};

use crate::php::fake_php_host::FakePhpHost;
use crate::sites::fake_site_host::{
    FakeSiteHost, create_test_site, distro, php_identity, php_input,
};

/// The vhost every test here acts on.
const VHOST: &str = "/etc/maran/nginx/sites/example.com.conf";

#[test]
fn deleting_removes_the_vhost_the_agent_owns() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    delete_site(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    assert!(host.config(Path::new(VHOST)).is_none());
}

#[test]
fn deleting_a_site_that_is_already_gone_is_not_found() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    delete_site(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    match delete_site(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    ) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
    // And nothing was run for the second call: no unlink, no reload.
    assert_eq!(host.writes(), 2);
}

#[test]
fn deleting_leaves_the_customers_files_alone() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    delete_site(
        &host,
        &FakePhpHost::empty(),
        distro(),
        &php_identity(),
        None,
    )
    .unwrap();

    // The document root holds the only copy of somebody's site. Removing a
    // vhost is reversible; deleting a home directory's contents is not, and it
    // is the account operations that own that decision.
    assert!(!host.created().is_empty());
}

#[test]
fn deleting_the_accounts_last_site_on_a_version_takes_that_pool_with_it() {
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    create_test_site(&host, &php_input()).unwrap();
    // Written onto the host this test reads back: `create_test_site` supplies a
    // PHP host of its own, so the pool creation wrote is not on this one.
    let pool = std::path::Path::new("/etc/php/8.3/fpm/pool.d/acme.conf");
    crate::php::write_pool(
        &php_host,
        distro(),
        &crate::php::model::pool_input::PoolInput {
            account: php_input().account,
            version: maran_agent_core::validation::php_version::PhpVersion::parse("8.3").unwrap(),
            max_children: 10,
            overrides: Vec::new(),
        },
    )
    .unwrap();

    delete_site(
        &host,
        &php_host,
        distro(),
        &php_identity(),
        Some(&maran_agent_core::validation::php_version::PhpVersion::parse("8.3").unwrap()),
    )
    .unwrap();

    assert!(
        php_host.config(pool).is_none(),
        "the pool the panel retired must be gone"
    );
    assert_eq!(php_host.removals(), 1);
}

#[test]
fn deleting_a_site_leaves_the_pool_alone_when_the_panel_did_not_retire_it() {
    // A pool is shared per account x version: two sites of the same account on
    // the same version have ONE pool and one worker budget. Removing it because
    // this site went would take the account's other sites off the air, and only
    // the panel holds the rows that say whether another site still needs it.
    let host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    create_test_site(&host, &php_input()).unwrap();

    delete_site(&host, &php_host, distro(), &php_identity(), None).unwrap();

    assert_eq!(
        php_host.removals(),
        0,
        "no pool may be touched unless the panel retired it"
    );
}
