//! Tests for [`remove_account_pools`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::model::pool_input::PoolInput;
use crate::php::{remove_account_pools, write_pool};

/// The account every test here cleans up after.
fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

/// Writes `account`'s pool at `version` on `host`.
fn write(host: &FakePhpHost, version: &str) {
    write_pool(
        host,
        distro(),
        &PoolInput {
            account: account(),
            version: PhpVersion::parse(version).unwrap(),
            max_children: 5,
            overrides: Vec::new(),
        },
    )
    .unwrap();
}

#[test]
fn every_version_the_account_ever_used_is_cleaned_up_not_only_the_current_one() {
    // The whole reason the closed supported set is asked rather than the caller:
    // the panel's row says what a site is bound to NOW and does not remember
    // that the account ran 8.1 for a year. A pool left over from a version
    // nothing currently uses is exactly the one that survives every targeted
    // cleanup and takes the host down at the next unrelated reload.
    let host = FakePhpHost::with_installed(&["8.1", "8.3", "8.4"]);
    write(&host, "8.1");
    write(&host, "8.3");

    remove_account_pools(&host, distro(), &account()).unwrap();

    assert!(
        host.config(Path::new("/etc/php/8.1/fpm/pool.d/acme.conf"))
            .is_none()
    );
    assert!(
        host.config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_none()
    );
    assert_eq!(host.removals(), 2, "only the two that existed are removed");
}

#[test]
fn an_account_that_never_ran_php_costs_no_reload_at_all() {
    // A static-only customer is the common case, and a deletion that restarted
    // six php-fpm masters to remove nothing would make every such deletion a
    // small outage for every other tenant.
    let host = FakePhpHost::with_installed(&["8.3"]);

    remove_account_pools(&host, distro(), &account()).unwrap();

    assert_eq!(host.removals(), 0);
    assert_eq!(host.commands(), Vec::<Vec<String>>::new());
}

#[test]
fn another_accounts_pool_on_the_same_version_is_left_alone() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    write(&host, "8.3");
    write_pool(
        &host,
        distro(),
        &PoolInput {
            account: AccountName::parse("neighbour").unwrap(),
            version: PhpVersion::parse("8.3").unwrap(),
            max_children: 5,
            overrides: Vec::new(),
        },
    )
    .unwrap();

    remove_account_pools(&host, distro(), &account()).unwrap();

    assert!(
        host.config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_none()
    );
    assert!(
        host.config(Path::new("/etc/php/8.3/fpm/pool.d/neighbour.conf"))
            .is_some(),
        "a deletion must not take a neighbour's pool with it"
    );
}
