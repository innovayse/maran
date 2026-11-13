//! Tests for [`list_installable_apps`]: the catalogue intersected with what the sources carry.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_distro::{DistroFamily, adapter_for};

use super::list_installable_apps;
use crate::apps::{AppsOpError, NodejsUnavailable};
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro, el10_distro};

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
        .apps
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

    let listed = list_installable_apps(&host, distro()).unwrap().apps;

    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0].id.as_id(), "apache");
    assert!(listed[0].installed);
}

#[test]
fn the_rhel_family_offers_apache_when_its_ssl_module_package_is_carried_too() {
    let host = FakePhpHost::empty();
    let dnf = "Available Packages\nhttpd.x86_64 2.4.57-8.el9 appstream\nmod_ssl.x86_64 1:2.4.57-8.el9 appstream\n";
    host.answer("-q", 0, dnf, "");

    let listed = list_installable_apps(&host, el10_distro()).unwrap().apps;
    let ids: Vec<String> = listed.iter().map(|app| app.id.as_id()).collect();

    assert_eq!(ids, vec!["apache"]);
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

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel))
        .unwrap()
        .apps;

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

/// A `dnf -C module list nodejs` of a RHEL 9 host: three streams, `common` among the profiles.
const MODULE_LIST: &str = "Last metadata expiration check: 0:10:00 ago on Thu 12 Nov 2026.
AlmaLinux 9 - AppStream
Name        Stream      Profiles                                   Summary
nodejs      18          common [d], development, minimal, s2i      Javascript runtime
nodejs      20          common [d], development, minimal, s2i      Javascript runtime
nodejs      22          common [d], development, minimal, s2i      Javascript runtime

Hint: [d]efault, [e]nabled, [x]disabled, [i]nstalled
";

#[test]
fn a_module_stream_family_offers_the_node_majors_its_module_list_carries() {
    let host = FakePhpHost::empty();
    host.answer("-C", 0, MODULE_LIST, "");
    host.answer("-q", 0, "Available Packages\n", "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel))
        .unwrap()
        .apps;
    let ids: Vec<String> = listed.iter().map(|app| app.id.as_id()).collect();

    assert_eq!(ids, vec!["nodejs-22", "nodejs-20", "nodejs-18"]);
}

#[test]
fn a_node_package_of_the_right_name_is_no_offer_on_a_module_stream_family() {
    // Inverse control: the package query says nodejs20 exists, the module list says nothing.
    let host = FakePhpHost::empty();
    host.answer(
        "-C",
        0,
        "Last metadata expiration check: 0:10:00 ago.\n",
        "",
    );
    host.answer(
        "-q",
        0,
        "Available Packages\nnodejs20.x86_64 20.11.1-1.el9 appstream\n",
        "",
    );

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel))
        .unwrap()
        .apps;

    assert!(listed.is_empty());
}

/// The apps of a RHEL host whose package query carries Apache (and so its TLS module).
const APACHE_AVAILABLE: &str = "Available Packages\nhttpd.x86_64 2.4.57-8.el9 appstream\nmod_ssl.x86_64 1:2.4.57-8.el9 appstream\n";

/// `dnf -C module list nodejs` standard error on `almalinux:10` with a warm cache (recorded).
const NO_MODULES_STDERR: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_module_list_nodejs_no_modules.stderr.txt"
);

/// The same command's standard error on `almalinux:9` with no metadata cache (recorded).
const NO_CACHE_STDERR: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_module_list_nodejs_no_cache.stderr.txt"
);

#[test]
fn a_module_list_that_fails_hides_node_js_only_and_says_why() {
    let host = FakePhpHost::empty();
    host.answer("-C", 1, "", NO_CACHE_STDERR);
    host.answer("-q", 0, APACHE_AVAILABLE, "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    // The web server is still listed; only Node.js is gone, and the reason is "could not ask".
    let ids: Vec<String> = listed.apps.iter().map(|app| app.id.as_id()).collect();
    assert_eq!(ids, vec!["apache"]);
    assert_eq!(
        listed.nodejs_unavailable,
        Some(NodejsUnavailable::ListingFailed)
    );
}

#[test]
fn sources_without_modules_hide_node_js_only_and_say_they_carry_none() {
    // The RHEL 10 generation: `Error: No matching Modules to list`, exit 1, nothing on stdout.
    let host = FakePhpHost::empty();
    host.answer("-C", 1, "", NO_MODULES_STDERR);
    host.answer("-q", 0, APACHE_AVAILABLE, "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    let ids: Vec<String> = listed.apps.iter().map(|app| app.id.as_id()).collect();
    assert_eq!(ids, vec!["apache"]);
    assert_eq!(
        listed.nodejs_unavailable,
        Some(NodejsUnavailable::NoModuleStreams)
    );
}

#[test]
fn a_module_list_that_works_has_no_unavailable_reason_and_offers_node_js_beside_apache() {
    // Inverse control of the two above: the same host with an answering listing.
    let host = FakePhpHost::empty();
    host.answer("-C", 0, MODULE_LIST, "");
    host.answer("-q", 0, APACHE_AVAILABLE, "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    let ids: Vec<String> = listed.apps.iter().map(|app| app.id.as_id()).collect();
    assert_eq!(ids, vec!["apache", "nodejs-22", "nodejs-20", "nodejs-18"]);
    assert_eq!(listed.nodejs_unavailable, None);
}

#[test]
fn an_installed_node_js_stays_listed_when_the_module_list_gives_no_answer() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");
    host.answer("-C", 1, "", NO_MODULES_STDERR);
    host.answer("-q", 0, "Available Packages\n", "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    let ids: Vec<String> = listed.apps.iter().map(|app| app.id.as_id()).collect();
    assert_eq!(ids, vec!["nodejs-20"]);
    assert_eq!(
        listed.nodejs_unavailable,
        Some(NodejsUnavailable::NoModuleStreams)
    );
}

#[test]
fn a_module_list_that_runs_past_its_deadline_is_still_an_error() {
    // A wedged package manager is not "Node.js is missing": nothing can be listed.
    let host = FakePhpHost::empty();
    host.time_out("-C");
    host.answer("-q", 0, "Available Packages\n", "");

    assert!(matches!(
        list_installable_apps(&host, adapter_for(DistroFamily::Rhel)),
        Err(AppsOpError::Package(PackageError::Bound(_)))
    ));
}

#[test]
fn an_installed_stream_stays_listed_when_the_module_list_no_longer_carries_it() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v18.19.1\n", "");
    host.answer(
        "-C",
        0,
        "Last metadata expiration check: 0:10:00 ago.\n",
        "",
    );
    host.answer("-q", 0, "Available Packages\n", "");

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel))
        .unwrap()
        .apps;
    let ids: Vec<String> = listed.iter().map(|app| app.id.as_id()).collect();

    assert_eq!(ids, vec!["nodejs-18"]);
}

/// `dnf -q -C list --available` for `nodejs`, `nodejs-npm`, `nodejs20`, `nodejs22`, `nodejs24`,
/// `nodejs24-npm`, `nodejs18`, `nodejs-npm18`, `nodejs22-npm` and `nodejs20-npm` on
/// `almalinux:10` (10.2): only four of the ten exist.
const EL10_AVAILABLE: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_list_available_nodejs_el10.txt"
);

fn ids_on(host: &FakePhpHost, adapter: &dyn maran_distro::DistroAdapter) -> Vec<String> {
    list_installable_apps(host, adapter)
        .unwrap()
        .apps
        .into_iter()
        .map(|app| app.id.as_id())
        .collect()
}

#[test]
fn the_rhel_10_generation_offers_the_node_majors_its_packages_carry_and_asks_no_module_list() {
    let host = FakePhpHost::empty();
    host.answer("-q", 0, EL10_AVAILABLE, "");

    let listed = list_installable_apps(&host, el10_distro()).unwrap();

    // `nodejs` is the default 22 and `nodejs24` is 24; the sources carry no 18 or 20.
    let ids: Vec<String> = listed.apps.iter().map(|app| app.id.as_id()).collect();
    assert_eq!(ids, vec!["nodejs-24", "nodejs-22"]);
    assert_eq!(listed.nodejs_unavailable, None);
    assert!(
        host.commands()
            .iter()
            .all(|call| !call.iter().any(|argument| argument == "module")),
        "the 10 generation has no module streams to ask about: {:?}",
        host.commands()
    );
}

#[test]
fn the_same_sources_offer_no_node_js_to_the_9_generation_which_asks_the_module_list() {
    // Inverse control: the 9 generation reads Node.js from the module list, not from packages,
    // so the package rows above are no offer there.
    let host = FakePhpHost::empty();
    host.answer("-q", 0, EL10_AVAILABLE, "");
    host.answer("-C", 1, "", NO_MODULES_STDERR);

    let listed = list_installable_apps(&host, adapter_for(DistroFamily::Rhel)).unwrap();

    assert!(listed.apps.is_empty());
    assert_eq!(
        listed.nodejs_unavailable,
        Some(NodejsUnavailable::NoModuleStreams)
    );
}

#[test]
fn a_rhel_10_host_whose_sources_carry_no_node_js_lists_the_rest_and_gives_no_unavailable_reason() {
    // The sources were asked and carry no Node.js: that is "nothing offered", not "could not look".
    let host = FakePhpHost::empty();
    host.answer("-q", 0, APACHE_AVAILABLE, "");

    let listed = list_installable_apps(&host, el10_distro()).unwrap();

    assert_eq!(listed.apps.len(), 1);
    assert_eq!(listed.apps[0].id.as_id(), "apache");
    assert_eq!(listed.nodejs_unavailable, None);
}

#[test]
fn a_rhel_10_node_major_that_is_installed_stays_listed_and_is_found_by_its_own_interpreter() {
    let adapter = el10_distro();
    let host = FakePhpHost::empty();
    host.answer("-q", 0, "Available Packages\n", "");
    host.answer_program(
        adapter
            .node_binary(&NodeMajor::parse("24").unwrap())
            .as_str(),
        0,
        "v24.22.1\n",
        "",
    );

    let listed = list_installable_apps(&host, adapter).unwrap().apps;

    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0].id.as_id(), "nodejs-24");
    assert!(listed[0].installed);
    // The other major is not mistaken for it: only `/usr/bin/node-24` answers.
    assert_eq!(ids_on(&host, adapter), vec!["nodejs-24"]);
}
