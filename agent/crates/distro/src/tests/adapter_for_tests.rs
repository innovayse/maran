//! Tests for the `adapter_for` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `adapter_for.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// `DistroAdapter` is deliberately not imported: `adapter_for` hands back a
// `dyn DistroAdapter`, and calling a method on a trait object does not need the
// trait in scope — importing it here was an unused import that failed the build.
use super::{DistroFamily, adapter_for};

#[test]
fn every_family_gets_the_adapter_it_asked_for() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        assert_eq!(adapter_for(family).family(), family);
    }
}

#[test]
fn the_families_disagree_about_where_a_php_pool_lives() {
    let debian = adapter_for(DistroFamily::Debian);
    let rhel = adapter_for(DistroFamily::Rhel);

    assert_eq!(
        debian.php_fpm_pool_directory("8.3"),
        "/etc/php/8.3/fpm/pool.d"
    );
    assert_eq!(
        rhel.php_fpm_pool_directory("8.3"),
        "/etc/opt/remi/php83/php-fpm.d"
    );
}

#[test]
fn the_rhel_family_drops_the_dot_from_a_php_version() {
    // The one difference a reader is most likely to get wrong, because the
    // package name and the path disagree with the version the caller passes.
    let rhel = adapter_for(DistroFamily::Rhel);

    assert_eq!(rhel.php_package("8.4"), "php84-php-fpm");
    assert_eq!(rhel.php_fpm_service("8.4"), "php84-php-fpm");
}

#[test]
fn the_web_server_runs_as_a_different_user_on_each_family() {
    assert_eq!(
        adapter_for(DistroFamily::Debian).web_server_user(),
        "www-data"
    );
    assert_eq!(adapter_for(DistroFamily::Rhel).web_server_user(), "nginx");
}

#[test]
fn the_web_server_belongs_to_a_different_group_on_each_family() {
    // Asked separately from the user, because an account's home is group-owned by
    // this name so a site under it can be served. The two names agreeing on both
    // families today is a fact about these distributions, not a rule.
    assert_eq!(
        adapter_for(DistroFamily::Debian).web_server_group(),
        "www-data"
    );
    assert_eq!(adapter_for(DistroFamily::Rhel).web_server_group(), "nginx");
}
