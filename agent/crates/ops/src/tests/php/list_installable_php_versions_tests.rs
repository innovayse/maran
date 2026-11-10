//! Tests for [`list_installable_php_versions`].
//!
//! The package manager's answers are the recordings the distro crate keeps in
//! `tests/fixtures/package_manager/`: a stock Ubuntu 24.04 that carries only
//! `php8.3-fpm`, an Ubuntu 24.04 with the Sury repository, and the output of a
//! never-built catalogue.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::{DistroFamily, adapter_for};

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::{PhpOpError, list_installable_php_versions};

/// `apt-cache policy` on a stock Ubuntu 24.04 after `apt-get update`.
const UBUNTU_ONLY_8_3: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/apt_policy_ubuntu_php83_only.txt"
);

/// `apt-cache policy` on an Ubuntu 24.04 with the Sury repository.
const SURY_ALL: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/apt_policy_sury_all_versions.txt"
);

/// `apt-get indextargets` against built lists: one index file per line.
const INDEX_BUILT: &str =
    "/var/lib/apt/lists/archive.ubuntu.com_ubuntu_dists_noble_main_binary-amd64_Packages\n";

/// A Debian-family host whose built catalogue answers `policy` with `policy`.
fn host_whose_sources_say(policy: &str) -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.answer("indextargets", 0, INDEX_BUILT, "");
    host.answer("policy", 0, policy, "");
    host
}

/// The versions listed, in order.
fn versions(host: &FakePhpHost) -> Vec<String> {
    list_installable_php_versions(host, distro())
        .unwrap()
        .into_iter()
        .map(|item| item.version)
        .collect()
}

#[test]
fn a_host_whose_sources_carry_only_php_8_3_lists_only_php_8_3() {
    let host = host_whose_sources_say(UBUNTU_ONLY_8_3);

    assert_eq!(versions(&host), vec!["8.3"]);
}

#[test]
fn a_host_whose_sources_carry_every_version_lists_all_six_newest_first() {
    // Inverse control: the list is not mutated to offer a single version.
    let host = host_whose_sources_say(SURY_ALL);

    assert_eq!(
        versions(&host),
        vec!["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"]
    );
}

#[test]
fn an_installed_version_stays_in_the_list_flagged_as_installed() {
    let host = host_whose_sources_say(SURY_ALL);
    for version in ["8.1", "8.3"] {
        host.place_installed(version);
    }

    let listed = list_installable_php_versions(&host, distro()).unwrap();

    let installed: Vec<&str> = listed
        .iter()
        .filter(|item| item.installed)
        .map(|item| item.version.as_str())
        .collect();
    assert_eq!(installed, vec!["8.3", "8.1"]);
    assert_eq!(listed.len(), 6);
}

#[test]
fn an_installed_version_the_sources_no_longer_carry_is_still_listed() {
    let host = host_whose_sources_say(UBUNTU_ONLY_8_3);
    host.place_installed("8.1");

    let listed = list_installable_php_versions(&host, distro()).unwrap();

    let pairs: Vec<(&str, bool)> = listed
        .iter()
        .map(|item| (item.version.as_str(), item.installed))
        .collect();
    assert_eq!(pairs, vec![("8.3", false), ("8.1", true)]);
}

#[test]
fn a_host_offering_nothing_lists_nothing() {
    let host = host_whose_sources_say("");

    assert!(versions(&host).is_empty());
}

#[test]
fn the_host_is_asked_once_with_one_read_only_query_for_all_six_packages() {
    let host = host_whose_sources_say(UBUNTU_ONLY_8_3);

    versions(&host);

    let commands = host.commands();
    assert_eq!(
        commands.len(),
        2,
        "catalogue probe, then one candidate query"
    );
    assert_eq!(commands[1][0], "/usr/bin/apt-cache");
    assert_eq!(&commands[1][1..3], ["policy", "--"]);
    assert_eq!(
        &commands[1][3..],
        [
            "php8.4-fpm",
            "php8.3-fpm",
            "php8.2-fpm",
            "php8.1-fpm",
            "php8.0-fpm",
            "php7.4-fpm"
        ]
    );
    assert!(
        host.classes()
            .iter()
            .all(|class| { *class == maran_agent_core::utils::call_class::CallClass::Query })
    );
}

#[test]
fn a_catalogue_that_was_never_built_is_an_error_and_not_an_empty_list() {
    let host = FakePhpHost::empty();
    host.answer("indextargets", 0, "", "");
    host.answer("policy", 0, "", "");

    match list_installable_php_versions(&host, distro()) {
        Err(PhpOpError::PackageCatalogueUnavailable { .. }) => {}
        other => panic!("expected PackageCatalogueUnavailable, got {other:?}"),
    }
}

#[test]
fn a_query_that_exits_with_a_failure_is_an_error_and_not_an_empty_list() {
    let host = host_whose_sources_say("");
    host.answer("policy", 100, "", "E: The package lists are broken");

    match list_installable_php_versions(&host, distro()) {
        Err(PhpOpError::PackageCatalogueUnavailable { reason }) => {
            assert!(reason.contains("100"), "{reason}");
            assert!(
                !reason.contains("broken"),
                "tool output must not travel: {reason}"
            );
        }
        other => panic!("expected PackageCatalogueUnavailable, got {other:?}"),
    }
}

#[test]
fn a_query_that_cannot_be_started_is_an_error() {
    let host = host_whose_sources_say(UBUNTU_ONLY_8_3);
    host.fail_to_spawn("policy");

    assert!(matches!(
        list_installable_php_versions(&host, distro()),
        Err(PhpOpError::PackageCatalogueUnavailable { .. })
    ));
}

#[test]
fn a_query_killed_at_its_deadline_is_a_bound_and_not_a_missing_catalogue() {
    let host = host_whose_sources_say(UBUNTU_ONLY_8_3);
    host.time_out("policy");

    assert!(matches!(
        list_installable_php_versions(&host, distro()),
        Err(PhpOpError::Bound(_))
    ));
}

#[test]
fn the_rhel_family_is_asked_through_dnf_and_lists_what_dnf_lists() {
    let rhel = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::empty();
    host.answer(
        "-q",
        0,
        "Available Packages\nphp83-php-fpm.x86_64   8.3.30-1.el9.remi   remi-safe\n",
        "",
    );

    let listed = list_installable_php_versions(&host, rhel).unwrap();

    let offered: Vec<&str> = listed.iter().map(|item| item.version.as_str()).collect();
    assert_eq!(offered, vec!["8.3"]);
    assert_eq!(host.commands()[0][0], "/usr/bin/dnf");
}

#[test]
fn the_rhel_family_with_no_matching_package_lists_nothing() {
    let rhel = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::empty();
    host.answer("-q", 1, "", "Error: No matching Packages to list\n");

    assert!(
        list_installable_php_versions(&host, rhel)
            .unwrap()
            .is_empty()
    );
}

/// What `zypper search --details --match-exact -t package` prints when the repositories carry
/// `php8-fpm`.
///
/// Constructed from `man zypper`, not recorded: no SUSE container exists in this phase, so the
/// recording on a real Leap image is owed and the layout is UNVERIFIED. Confirm with
/// `zypper --non-interactive --no-refresh search --details --match-exact -t package -- php8-fpm`.
const ZYPPER_CARRIES_PHP8_FPM: &str = "\
Loading repository data...
Reading installed packages...

S  | Name     | Type    | Version    | Arch   | Repository
---+----------+---------+------------+--------+----------------
   | php8-fpm | package | 8.4.16-1.1 | x86_64 | Main Repository
";

/// What `zypper search` prints, with status 104, when no queried name matched.
const ZYPPER_CARRIES_NOTHING: &str = "\
Loading repository data...
Reading installed packages...
No matching items found.
";

#[test]
fn the_suse_family_is_asked_through_zypper_and_lists_the_one_version_the_package_manager_offers() {
    let suse = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer("--non-interactive", 0, ZYPPER_CARRIES_PHP8_FPM, "");

    let listed = list_installable_php_versions(&host, suse).unwrap();

    // Every other supported version is asked for under a dotted package name no repository
    // carries, so the one distribution minor is all that is offered, and nothing is installed.
    let offered: Vec<(&str, bool)> = listed
        .iter()
        .map(|item| (item.version.as_str(), item.installed))
        .collect();
    assert_eq!(offered, vec![("8.4", false)]);
    assert_eq!(
        host.commands().len(),
        1,
        "the family needs no catalogue probe"
    );
    assert_eq!(host.commands()[0][0], "/usr/bin/zypper");
}

#[test]
fn the_suse_family_with_no_matching_package_lists_nothing() {
    let suse = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer("--non-interactive", 104, ZYPPER_CARRIES_NOTHING, "");

    assert!(
        list_installable_php_versions(&host, suse)
            .unwrap()
            .is_empty()
    );
}

#[test]
fn the_suse_family_with_a_skipped_repository_is_unavailable_and_never_an_empty_list() {
    let suse = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer(
        "--non-interactive",
        106,
        "",
        "Some of the repositories have not been refreshed.\n",
    );

    assert!(matches!(
        list_installable_php_versions(&host, suse),
        Err(PhpOpError::PackageCatalogueUnavailable { .. })
    ));
}
