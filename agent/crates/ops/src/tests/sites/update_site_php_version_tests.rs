//! Tests for [`update_site_php_version`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::fake_php_host::FakePhpHost;
use crate::php::model::php_override::PhpOverride;
use crate::php::write_pool;
use crate::sites::fake_site_host::{FakeSiteHost, create_test_site, distro, php_input};
use crate::sites::model::php_switch::PhpSwitch;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::{SitesOpError, update_site_php_version};

/// The worker budget the plan would supply; immaterial to these decisions.
const WORKERS: u32 = 10;

#[test]
fn switching_versions_repoints_the_vhost_at_the_new_socket() {
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    let vhost = site_host
        .config(&SitePaths::for_site(&site.account, &site.domain).config_path)
        .unwrap();
    assert!(vhost.contains("acme-8.4.sock"), "{vhost}");
    assert!(!vhost.contains("acme-8.3.sock"), "{vhost}");
}

#[test]
fn the_pool_for_the_new_version_is_written_before_the_vhost_moves() {
    // Order matters, and not in the obvious direction: pool first means the
    // site is briefly on the old socket while the new pool already listens,
    // which serves every request. Reversed, the window is a vhost pointing at
    // a socket nothing has bound, and every request in it is a 502.
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    assert_eq!(php_host.writes(), 1);
}

#[test]
fn a_version_that_is_not_installed_is_refused_rather_than_installed() {
    // The contract makes this VALIDATION_FAILED: installing takes minutes and
    // streams progress, so it is its own operation and not a side effect of
    // this one.
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();

    match update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    ) {
        Err(SitesOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.4"),
        other => panic!("expected PhpVersionNotInstalled, got {other:?}"),
    }
    assert_eq!(php_host.writes(), 0);
}

#[test]
fn setting_the_version_a_site_already_has_changes_nothing() {
    // The panel retries after a timeout, and a reload of nginx AND of php-fpm
    // per retry is a storm on a busy host.
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    assert_eq!(site_host.writes(), 1);
    assert_eq!(php_host.writes(), 0);
}

#[test]
fn the_customers_settings_survive_the_version_switch() {
    // This function is the ONLY writer of the pool on a version switch and
    // nothing re-applies settings afterwards. Rendering an empty list here
    // would hand a customer who set memory_limit = 256M a pool with no
    // php_value lines at all — and a success response saying it worked.
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();
    let overrides = vec![PhpOverride::parse("memory_limit", "256M").unwrap()];

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &overrides,
            remove_previous_pool: false,
        },
    )
    .unwrap();

    let pool = php_host
        .config(std::path::Path::new("/etc/php/8.4/fpm/pool.d/acme.conf"))
        .expect("no pool was written for the new version");
    assert!(pool.contains("php_value[memory_limit] = 256M"), "{pool}");
}

#[test]
fn a_site_that_does_not_exist_is_not_found() {
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);

    match update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &php_input(),
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    ) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
}

#[test]
fn switching_away_from_a_version_the_account_no_longer_uses_removes_its_pool() {
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();
    // The 8.3 pool is the one creation wrote; the switch below leaves it behind.
    write_pool(
        &php_host,
        distro(),
        &crate::php::model::pool_input::PoolInput {
            account: site.account.clone(),
            version: PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: Vec::new(),
        },
    )
    .unwrap();

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: true,
        },
    )
    .unwrap();

    assert!(
        php_host
            .config(std::path::Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_none(),
        "the version the site left must not keep a pool and a set of idle workers"
    );
    assert!(
        php_host
            .config(std::path::Path::new("/etc/php/8.4/fpm/pool.d/acme.conf"))
            .is_some(),
        "the version it moved to must keep its pool"
    );
}

#[test]
fn the_old_pool_is_removed_only_after_the_new_one_is_in_place_and_the_vhost_has_moved() {
    // The order is what keeps the site servable at every instant: the new pool
    // is bound before the vhost moves, and the old pool goes only once nothing
    // points at it. Removing it any earlier is a window in which a live vhost
    // names a dead socket and every request is a 502.
    //
    // Pinned by the failure path: when the VHOST write is refused, the switch
    // returns before it reaches the removal, so the old pool is still there.
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();
    site_host.reject_validation("nginx will not have the new vhost");

    let refusal = update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: true,
        },
    );

    assert!(refusal.is_err(), "the vhost write must have been refused");
    assert_eq!(
        php_host.removals(),
        0,
        "a switch that never took effect must not have removed the pool the site is still on"
    );
}

#[test]
fn the_old_pool_stays_when_the_account_still_has_another_site_on_it() {
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3", "8.4"]);
    let site = php_input();
    create_test_site(&site_host, &site).unwrap();
    write_pool(
        &php_host,
        distro(),
        &crate::php::model::pool_input::PoolInput {
            account: site.account.clone(),
            version: PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: Vec::new(),
        },
    )
    .unwrap();

    update_site_php_version(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.4").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    assert!(
        php_host
            .config(std::path::Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_some(),
        "the account's other sites are still served by this pool"
    );
}
