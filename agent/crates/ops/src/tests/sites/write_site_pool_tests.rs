//! Tests for [`write_site_pool`].
//!
//! What this unit decides is small and worth pinning exactly: it writes the
//! pool for the version it was GIVEN, not for the one the site currently is,
//! and it reports the PHP area's refusals as this area's errors so a caller
//! never has to follow a chain of `#[from]`s to find out what happened.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::fake_php_host::FakePhpHost;
use crate::php::model::php_override::PhpOverride;
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{TEST_WORKERS, php_input};
use crate::sites::write_site_pool::write_site_pool;

#[test]
fn the_pool_is_written_for_the_version_the_caller_named_not_the_one_the_site_is() {
    // The switch's whole business is that the two disagree: the site is still
    // 8.3 while the pool being written is the 8.4 one it is moving to.
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();

    write_site_pool(
        &php_host,
        crate::sites::fake_site_host::distro(),
        &site,
        &PhpVersion::parse("8.4").unwrap(),
        TEST_WORKERS,
        &[],
    )
    .unwrap();

    let pool = php_host
        .config(Path::new("/etc/php/8.4/fpm/pool.d/acme.conf"))
        .expect("the pool must be written into the named version's own directory");
    assert!(pool.contains("acme-8.4.sock"), "{pool}");
}

#[test]
fn the_plans_worker_budget_reaches_the_pool_as_pm_max_children() {
    let php_host = FakePhpHost::with_installed(&["8.3"]);

    write_site_pool(
        &php_host,
        crate::sites::fake_site_host::distro(),
        &php_input(),
        &PhpVersion::parse("8.3").unwrap(),
        7,
        &[],
    )
    .unwrap();

    let pool = php_host
        .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
        .unwrap();
    assert!(pool.contains("pm.max_children = 7"), "{pool}");
}

#[test]
fn the_customers_whitelisted_settings_are_carried_into_the_pool_not_dropped() {
    // Refuse-don't-drop: a pool written from scratch that omitted a setting the
    // customer had set would report success while quietly changing what their
    // site runs with.
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let setting = PhpOverride::parse("memory_limit", "256M").unwrap();

    write_site_pool(
        &php_host,
        crate::sites::fake_site_host::distro(),
        &php_input(),
        &PhpVersion::parse("8.3").unwrap(),
        TEST_WORKERS,
        std::slice::from_ref(&setting),
    )
    .unwrap();

    let pool = php_host
        .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
        .unwrap();
    assert!(pool.contains("memory_limit"), "{pool}");
    assert!(pool.contains("256M"), "{pool}");
}

#[test]
fn a_version_this_host_does_not_have_is_reported_as_this_areas_own_refusal() {
    let php_host = FakePhpHost::with_installed(&["8.3"]);

    let refusal = write_site_pool(
        &php_host,
        crate::sites::fake_site_host::distro(),
        &php_input(),
        &PhpVersion::parse("8.4").unwrap(),
        TEST_WORKERS,
        &[],
    );

    match refusal {
        Err(SitesOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.4"),
        other => panic!("expected a sites-level PhpVersionNotInstalled, got {other:?}"),
    }
}
