//! Tests for [`remove_pool`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php::fake_php_host::{FakePhpHost, distro, pool_input};
use crate::php::{PhpOpError, remove_pool, write_pool};

/// The pool file the fixtures write, in the version's own directory.
const POOL: &str = "/etc/php/8.3/fpm/pool.d/acme.conf";

/// The account every test here removes a pool for.
fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

/// The version every test here removes a pool at.
fn version() -> PhpVersion {
    PhpVersion::parse("8.3").unwrap()
}

#[test]
fn a_written_pool_is_taken_away_again() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();
    assert!(host.config(Path::new(POOL)).is_some());

    remove_pool(&host, distro(), &account(), &version()).unwrap();

    assert!(
        host.config(Path::new(POOL)).is_none(),
        "the pool file must be gone: a pool naming a deleted account is what makes the next \
         php-fpm reload fail for every tenant on the host"
    );
}

#[test]
fn a_pool_that_was_never_written_is_a_success_that_reloads_nothing() {
    // Every caller is in this position: a pool exists only if the account ever
    // used that version, and no caller knows which versions it used. Reloading
    // a php-fpm master to reach a state it is already in is a restart storm on
    // an account deletion.
    let host = FakePhpHost::with_installed(&["8.3"]);

    remove_pool(&host, distro(), &account(), &version()).unwrap();

    assert_eq!(host.removals(), 0);
    assert_eq!(host.commands(), Vec::<Vec<String>>::new());
}

#[test]
fn a_pool_the_real_php_fpm_would_refuse_to_lose_is_put_back() {
    // The removal protocol validates AFTER unlinking and restores the file when
    // validation refuses, so a refusal must leave the pool exactly where it was
    // rather than half-removed.
    let host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();
    host.reject_validation("something else in the tree needs this pool");

    let refusal = remove_pool(&host, distro(), &account(), &version());

    assert!(
        matches!(refusal, Err(PhpOpError::PoolValidation { .. })),
        "expected the validator's refusal, got {refusal:?}"
    );
    assert!(
        host.config(Path::new(POOL)).is_some(),
        "the pool must be back"
    );
}

#[test]
fn a_version_outside_the_supported_set_is_refused_before_any_path_is_built() {
    // The path is a join of the account name and the version, and an unlink is
    // the one operation whose mistakes nobody notices: a write that escaped its
    // directory leaves a file somebody finds, a removal that escaped it destroys
    // one nobody does.
    let host = FakePhpHost::with_installed(&["8.3"]);

    let refusal = remove_pool(
        &host,
        distro(),
        &account(),
        &PhpVersion::parse("9.9").unwrap(),
    );

    assert!(
        matches!(refusal, Err(PhpOpError::UnsupportedVersion { .. })),
        "expected UnsupportedVersion, got {refusal:?}"
    );
    assert_eq!(host.removals(), 0);
}
