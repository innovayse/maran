//! Tests for [`list_installable_php_versions`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::list_installable_php_versions;

#[test]
fn every_supported_version_is_offered_newest_first() {
    let host = FakePhpHost::empty();

    let listed = list_installable_php_versions(&host, distro()).unwrap();

    let versions: Vec<&str> = listed.iter().map(|item| item.version.as_str()).collect();
    assert_eq!(versions, vec!["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"]);
}

#[test]
fn an_installed_version_stays_in_the_list_flagged_as_installed() {
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);

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
fn a_host_with_no_php_flags_nothing_as_installed() {
    let host = FakePhpHost::empty();

    let listed = list_installable_php_versions(&host, distro()).unwrap();

    assert!(listed.iter().all(|item| !item.installed));
}

#[test]
fn listing_runs_no_command_at_all() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    list_installable_php_versions(&host, distro()).unwrap();

    assert!(host.commands().is_empty());
}
