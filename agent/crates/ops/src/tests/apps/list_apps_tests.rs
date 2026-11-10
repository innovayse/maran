//! Tests for [`list_apps`]: probed, in catalogue order, with the service status of each.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::list_apps;
use crate::apps::{AppKind, AppServiceStatus};
use crate::php::fake_php_host::{FakePhpHost, distro};

fn ids(host: &FakePhpHost) -> Vec<String> {
    list_apps(host, distro())
        .unwrap()
        .into_iter()
        .map(|app| app.id.as_id())
        .collect()
}

#[test]
fn a_host_with_nothing_installed_lists_nothing() {
    assert!(ids(&FakePhpHost::empty()).is_empty());
}

#[test]
fn installed_apps_are_listed_in_catalogue_order_with_php_newest_first() {
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    host.answer("--version", 0, "v22.2.0\n", "");

    assert_eq!(ids(&host), vec!["nginx", "php-8.3", "php-8.1", "nodejs-22"]);
}

#[test]
fn the_kind_and_the_service_status_are_what_the_host_reports() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");
    host.answer("--version", 0, "v20.11.1\n", "");
    host.answer("is-active", 3, "failed\n", "");

    let apps = list_apps(&host, distro()).unwrap();

    assert_eq!(apps[0].kind, AppKind::WebServer);
    assert_eq!(apps[0].service_status, AppServiceStatus::Failed);
    assert_eq!(apps[1].kind, AppKind::Runtime);
    assert_eq!(apps[1].version, "8.3");
    // Node.js has no service, so the service manager is not asked about it.
    let node = apps.last().unwrap();
    assert_eq!(node.id.as_id(), "nodejs-20");
    assert_eq!(node.service_status, AppServiceStatus::None);
}

#[test]
fn an_active_service_is_active_and_any_other_word_is_inactive() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    host.answer("is-active", 0, "active\n", "");
    assert_eq!(
        list_apps(&host, distro()).unwrap()[0].service_status,
        AppServiceStatus::Active
    );

    host.answer("is-active", 3, "inactive\n", "");
    assert_eq!(
        list_apps(&host, distro()).unwrap()[0].service_status,
        AppServiceStatus::Inactive
    );
}

#[test]
fn a_service_manager_that_will_not_start_fails_the_listing_and_does_not_shorten_it() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    host.fail_to_spawn("is-active");

    assert!(list_apps(&host, distro()).is_err());
}

#[test]
fn nginx_present_as_the_panels_front_is_not_a_listed_web_server_until_it_is_chosen() {
    // The fresh-host control: the package is there (it is the panel's front) and the host has
    // chosen nothing, so no web server is listed and the first-run setup must happen.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    assert!(ids(&host).is_empty());

    // Inverse control: the same host once the choice is recorded lists it.
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    assert_eq!(ids(&host), vec!["nginx"]);
}

#[test]
fn apache_needs_both_the_marker_and_its_binary_to_be_listed() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");
    // A marker whose package was removed by hand is not reported.
    assert!(ids(&host).is_empty());

    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    assert_eq!(ids(&host), vec!["apache"]);
}
