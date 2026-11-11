//! Tests for the installed probe: what makes an app installed, and the version text.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroFamily, adapter_for};

use super::{installed_version, is_installed, version_after};
use crate::php::fake_php_host::{FakePhpHost, distro};

fn id(text: &str) -> ValidatedAppId {
    ValidatedAppId::parse(text).unwrap()
}

#[test]
fn a_web_server_is_installed_when_the_marker_names_it_and_its_binary_is_there() {
    let host = FakePhpHost::empty();
    assert!(!is_installed(&host, distro(), &id("apache")).unwrap());

    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    // The binary alone is not a choice.
    assert!(!is_installed(&host, distro(), &id("apache")).unwrap());

    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");

    assert!(is_installed(&host, distro(), &id("apache")).unwrap());
    // Inverse control: Apache's marker does not make nginx installed.
    assert!(!is_installed(&host, distro(), &id("nginx")).unwrap());
}

#[test]
fn nginx_on_disk_is_not_installed_without_the_marker() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    assert!(!is_installed(&host, distro(), &id("nginx")).unwrap());

    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    assert!(is_installed(&host, distro(), &id("nginx")).unwrap());
}

#[test]
fn a_php_version_is_installed_when_its_pool_directory_is() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    assert!(is_installed(&host, distro(), &id("php-8.3")).unwrap());
    assert!(!is_installed(&host, distro(), &id("php-8.2")).unwrap());
}

#[test]
fn a_node_major_is_installed_only_when_the_interpreter_reports_that_major() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");

    assert!(is_installed(&host, distro(), &id("nodejs-20")).unwrap());
    assert!(!is_installed(&host, distro(), &id("nodejs-22")).unwrap());
    // `v2` is a prefix of `v20`, which a text prefix test would accept.
    assert!(!is_installed(&host, distro(), &id("nodejs-18")).unwrap());
}

#[test]
fn a_second_node_major_beside_the_first_is_found_by_its_own_interpreter() {
    // The per-major families install each major as a file of its own. Asking the one `node`
    // would report 20 for both questions, and the install of 22 would read as StillAbsent.
    let adapter = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer_program("/usr/bin/node20", 0, "v20.11.1\n", "");
    host.answer_program("/usr/bin/node22", 0, "v22.3.0\n", "");

    assert!(is_installed(&host, adapter, &id("nodejs-20")).unwrap());
    assert!(is_installed(&host, adapter, &id("nodejs-22")).unwrap());
    assert_eq!(
        installed_version(&host, adapter, &id("nodejs-22")).unwrap(),
        "22.3.0"
    );
}

#[test]
fn a_node_major_whose_own_interpreter_is_missing_is_absent_although_another_major_runs() {
    // Inverse control: only 20 is installed. 22 must not be reported through 20's file.
    let adapter = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer_program("/usr/bin/node20", 0, "v20.11.1\n", "");
    host.answer_program("/usr/bin/node22", 1, "", "");

    assert!(is_installed(&host, adapter, &id("nodejs-20")).unwrap());
    assert!(!is_installed(&host, adapter, &id("nodejs-22")).unwrap());
    assert!(
        host.commands()
            .iter()
            .any(|call| call[0] == "/usr/bin/node22")
    );
}

#[test]
fn a_missing_interpreter_means_no_node_major_is_installed() {
    let host = FakePhpHost::empty();
    host.fail_to_spawn("--version");

    assert!(!is_installed(&host, distro(), &id("nodejs-20")).unwrap());
}

#[test]
fn a_node_version_is_read_without_its_leading_v() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");

    assert_eq!(
        installed_version(&host, distro(), &id("nodejs-20")).unwrap(),
        "20.11.1"
    );
}

#[test]
fn nginx_prints_its_version_on_standard_error() {
    let host = FakePhpHost::empty();
    host.answer("-v", 0, "", "nginx version: nginx/1.24.0 (Ubuntu)\n");

    assert_eq!(
        installed_version(&host, distro(), &id("nginx")).unwrap(),
        "1.24.0"
    );
}

#[test]
fn apache_prints_its_version_on_standard_output() {
    let host = FakePhpHost::empty();
    host.answer("-v", 0, "Server version: Apache/2.4.58 (Ubuntu)\n", "");

    assert_eq!(
        installed_version(&host, distro(), &id("apache")).unwrap(),
        "2.4.58"
    );
}

#[test]
fn a_version_that_cannot_be_read_is_empty_and_not_an_error() {
    let host = FakePhpHost::empty();
    host.fail_to_spawn("-v");

    assert_eq!(
        installed_version(&host, distro(), &id("apache")).unwrap(),
        ""
    );
}

#[test]
fn the_version_is_the_run_of_digits_and_dots_after_the_marker() {
    assert_eq!(
        version_after("x Apache/2.4.58 (Ubuntu)", "Apache/"),
        "2.4.58"
    );
    assert_eq!(version_after("no marker here", "Apache/"), "");
}
