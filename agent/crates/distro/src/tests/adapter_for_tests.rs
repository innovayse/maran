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

/// Every adapter this crate can hand out, so a fact required of ALL of them is asserted
/// against all of them rather than against whichever one the author had in mind.
fn every_adapter() -> [&'static dyn crate::DistroAdapter; 2] {
    [
        adapter_for(DistroFamily::Debian),
        adapter_for(DistroFamily::Rhel),
    ]
}

#[test]
fn the_mysql_client_is_an_absolute_path_on_every_family() {
    // Processes are spawned with argv arrays, never through a shell, so there is no PATH
    // lookup to fall back on: a relative name is a binary that simply fails to spawn.
    for adapter in every_adapter() {
        assert!(
            adapter.mysql_client_binary().starts_with('/'),
            "{:?} must name an absolute path: argv spawning has no PATH to fall back on",
            adapter.family()
        );
    }
}

#[test]
fn the_sftp_group_is_the_same_name_on_every_family_so_the_sshd_block_is_portable() {
    // The `Match Group` block the installer writes names one group. If the two families
    // answered different group names, the block written on one would chroot nobody on the
    // other, and an SFTP user created there would get a full session instead of a jail —
    // the opposite of the isolation it exists for. So here the invariant is SAMENESS,
    // asserted deliberately, not difference.
    assert_eq!(
        adapter_for(DistroFamily::Debian).sftp_group(),
        adapter_for(DistroFamily::Rhel).sftp_group()
    );
}

#[test]
fn both_families_restart_the_same_database_unit_because_both_ship_mariadb() {
    // Agreement stated rather than left for a reader to suspect a copy-paste. If a family
    // ever ships MySQL proper instead, this is the one method that changes.
    assert_eq!(
        adapter_for(DistroFamily::Debian).mysql_service(),
        adapter_for(DistroFamily::Rhel).mysql_service()
    );
}

#[test]
fn every_family_names_the_user_management_tools_by_an_absolute_path() {
    // A bare program name would be resolved through `PATH` by a root daemon, which is worse
    // than the literal it would have replaced: whoever controls the environment then chooses
    // which `useradd` runs as root.
    for adapter in every_adapter() {
        for binary in [
            adapter.useradd_binary(),
            adapter.userdel_binary(),
            adapter.chpasswd_binary(),
        ] {
            assert!(binary.starts_with('/'), "{binary} is not an absolute path");
        }
    }
}

#[test]
fn every_family_writes_its_unit_files_where_an_administrator_puts_them() {
    // The agent's own units must outrank anything a package ships, which is what the
    // administrator's unit directory is for.
    for adapter in every_adapter() {
        assert_eq!(adapter.systemd_unit_directory(), "/etc/systemd/system");
    }
}
