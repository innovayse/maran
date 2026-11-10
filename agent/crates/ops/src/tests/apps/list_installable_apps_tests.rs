//! Tests for [`list_installable_apps`]: the catalogue intersected with what the sources carry.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_distro::{DistroFamily, adapter_for};

use super::list_installable_apps;
use crate::apps::AppsOpError;
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro};

/// `apt-get indextargets` against built lists.
const INDEX_BUILT: &str =
    "/var/lib/apt/lists/archive.ubuntu.com_ubuntu_dists_noble_main_binary-amd64_Packages\n";

/// One `apt-cache policy` stanza with a candidate.
fn stanza(package: &str, candidate: &str) -> String {
    format!("{package}:\n  Installed: (none)\n  Candidate: {candidate}\n  Version table:\n")
}

/// A Debian-family host whose built catalogue answers `policy` with `policy`.
fn host_whose_sources_say(policy: &str) -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.answer("indextargets", 0, INDEX_BUILT, "");
    host.answer("policy", 0, policy, "");
    host
}

fn ids(host: &FakePhpHost) -> Vec<String> {
    list_installable_apps(host, distro())
        .unwrap()
        .into_iter()
        .map(|app| app.id.as_id())
        .collect()
}

#[test]
fn only_apps_whose_packages_the_sources_carry_are_listed() {
    let host = host_whose_sources_say(&format!(
        "{}{}",
        stanza("nginx", "1.24.0"),
        stanza("php8.3-fpm", "8.3.6")
    ));

    assert_eq!(ids(&host), vec!["nginx", "php-8.3"]);
}

#[test]
fn the_one_debian_nodejs_package_offers_exactly_the_major_it_ships() {
    let host = host_whose_sources_say(&stanza("nodejs", "18.19.1+dfsg-6"));

    assert_eq!(ids(&host), vec!["nodejs-18"]);
}

#[test]
fn a_nodejs_package_with_no_candidate_offers_no_major() {
    // Inverse control: the package being NAMED in the answer is not the package being offered.
    let host = host_whose_sources_say("nodejs:\n  Installed: (none)\n  Candidate: (none)\n");

    assert!(ids(&host).is_empty());
}

#[test]
fn an_installed_app_stays_listed_even_when_the_sources_no_longer_carry_it() {
    let host = host_whose_sources_say("");
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");

    let listed = list_installable_apps(&host, distro()).unwrap();

    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0].id.as_id(), "apache");
    assert!(listed[0].installed);
}

#[test]
fn the_rhel_family_asks_for_per_major_packages_and_apaches_ssl_module() {
    let host = FakePhpHost::empty();
    let dnf = "Available Packages\nhttpd.x86_64 2.4.57-8.el9 appstream\nmod_ssl.x86_64 1:2.4.57-8.el9 appstream\nnodejs20.x86_64 20.11.1-1.el9 appstream\n";
    host.answer("-q", 0, dnf, "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();
    let ids: Vec<String> = listed.iter().map(|app| app.id.as_id()).collect();

    assert_eq!(ids, vec!["apache", "nodejs-20"]);
}

#[test]
fn an_apache_without_its_ssl_package_is_not_offered_on_the_rhel_family() {
    let host = FakePhpHost::empty();
    host.answer(
        "-q",
        0,
        "Available Packages\nhttpd.x86_64 2.4.57-8.el9 appstream\n",
        "",
    );

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    assert!(listed.is_empty());
}

#[test]
fn a_catalogue_that_was_never_built_is_an_error_and_not_an_empty_list() {
    let host = FakePhpHost::empty();
    host.answer("indextargets", 0, "", "");

    assert!(matches!(
        list_installable_apps(&host, distro()),
        Err(AppsOpError::Package(
            PackageError::CatalogueUnavailable { .. }
        ))
    ));
}
