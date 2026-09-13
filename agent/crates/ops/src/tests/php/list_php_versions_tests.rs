//! Tests for [`list_php_versions`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::list_php_versions;

#[test]
fn only_the_versions_whose_pool_directories_exist_are_reported() {
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);

    let listed = list_php_versions(&host, distro()).unwrap();

    let versions: Vec<&str> = listed.iter().map(|item| item.version.as_str()).collect();
    assert_eq!(versions, vec!["8.3", "8.1"]);
}

#[test]
fn the_newest_version_comes_first() {
    // Newest first is the order a picker wants and the order a default is
    // taken from. It comes from the fixed supported list, not from sorting:
    // version strings compare wrongly as text — "8.10" sorts below "8.9" —
    // and this is a list a future 8.10 joins.
    let host = FakePhpHost::with_installed(&["7.4", "8.0", "8.4"]);

    let listed = list_php_versions(&host, distro()).unwrap();

    let versions: Vec<&str> = listed.iter().map(|item| item.version.as_str()).collect();
    assert_eq!(versions, vec!["8.4", "8.0", "7.4"]);
}

#[test]
fn a_host_with_no_php_reports_an_empty_list_rather_than_failing() {
    // The panel calls this on every page with a version picker, including on a
    // freshly installed server. An error there would be a broken page rather
    // than an empty dropdown.
    let host = FakePhpHost::empty();

    assert!(list_php_versions(&host, distro()).unwrap().is_empty());
}

#[test]
fn every_listed_version_names_the_socket_directory_the_vhost_uses() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    let listed = list_php_versions(&host, distro()).unwrap();

    assert_eq!(
        listed[0].socket_directory,
        AgentPaths::PHP_FPM_SOCKET_DIRECTORY
    );
    assert_eq!(listed[0].pool_directory, "/etc/php/8.3/fpm/pool.d");
}

#[test]
fn listing_runs_no_command_at_all() {
    // Deliberate, and the reason installed-ness is read from the filesystem:
    // `dpkg-query`/`rpm -q` per version per page load takes the package
    // database lock a real installation elsewhere on the host is waiting for.
    let host = FakePhpHost::with_installed(&["8.3"]);

    list_php_versions(&host, distro()).unwrap();

    assert!(host.commands().is_empty());
}
