//! Tests for [`remove_site_pool`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::fake_php_host::{FakePhpHost, distro, pool_input};
use crate::php::write_pool;
use crate::sites::SitesOpError;
use crate::sites::remove_site_pool::remove_site_pool;

#[test]
fn the_pool_is_removed_for_the_account_and_version_the_caller_named() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    remove_site_pool(
        &host,
        distro(),
        &AccountName::parse("acme").unwrap(),
        &PhpVersion::parse("8.3").unwrap(),
    )
    .unwrap();

    assert!(
        host.config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_none()
    );
}

#[test]
fn the_php_areas_refusal_is_reported_as_this_areas_own() {
    // A caller in the sites area must not have to follow a chain of `#[from]`s
    // into another area to find out what happened.
    let host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();
    host.reject_validation("php-fpm says no");

    let refusal = remove_site_pool(
        &host,
        distro(),
        &AccountName::parse("acme").unwrap(),
        &PhpVersion::parse("8.3").unwrap(),
    );

    assert!(
        matches!(refusal, Err(SitesOpError::ConfigWrite { .. })),
        "expected a sites-level ConfigWrite, got {refusal:?}"
    );
}
