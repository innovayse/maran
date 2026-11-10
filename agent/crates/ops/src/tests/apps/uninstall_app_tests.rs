//! Tests for [`uninstall_app`]: refused while in use, never the panel's own nginx.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use super::uninstall_app;
use crate::apps::AppsOpError;
use crate::php::fake_php_host::{FakePhpHost, distro};

fn id(text: &str) -> ValidatedAppId {
    ValidatedAppId::parse(text).unwrap()
}

fn uninstall(host: &FakePhpHost, app: &str) -> Result<(), AppsOpError> {
    uninstall_app(host, distro(), &id(app), |_, _| {})
}

fn started_units(host: &FakePhpHost) -> Vec<String> {
    host.commands()
        .into_iter()
        .filter(|call| call.get(1).map(String::as_str) == Some("start"))
        .filter_map(|call| call.last().cloned())
        .collect()
}

#[test]
fn an_app_that_is_not_installed_is_not_installed_and_no_unit_starts() {
    let host = FakePhpHost::empty();

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::NotInstalled { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn apache_is_removed_through_the_removal_unit_of_its_package() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");
    host.removes_files_on_start(&["/usr/sbin/apache2ctl"]);

    uninstall(&host, "apache").unwrap();

    assert_eq!(
        started_units(&host),
        vec!["maran-app-remove@apache2.service"]
    );
}

#[test]
fn apache_with_a_site_is_in_use_and_nothing_is_started() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");
    host.place_config(
        Path::new("/etc/maran/apache/sites/example.com.conf"),
        "<VirtualHost>",
    );

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::InUse { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_php_version_with_an_account_pool_is_in_use_and_the_packaged_pool_is_not_a_user() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let pool_directory = distro().php_fpm_pool_directory("8.3");
    host.place_config(&Path::new(&pool_directory).join("www.conf"), "");

    // Positive control: only the packaged pool, so the version may go.
    host.removes_files_on_start(&[]);
    let removed = uninstall(&host, "php-8.3");
    assert!(
        matches!(removed, Err(AppsOpError::StillAbsent { .. })),
        "{removed:?}"
    );

    host.place_config(&Path::new(&pool_directory).join("acc12345.conf"), "");
    assert!(matches!(
        uninstall(&host, "php-8.3"),
        Err(AppsOpError::InUse { .. })
    ));
}

#[test]
fn nginx_is_never_removed_it_is_the_panels_front() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");

    uninstall(&host, "nginx").unwrap();

    assert!(started_units(&host).is_empty());
    // Only the choice goes: the package is untouched and the marker is gone.
    assert!(host.config(Path::new("/usr/sbin/nginx")).is_some());
    assert!(
        host.config(Path::new("/etc/maran/site-web-server"))
            .is_none()
    );
}

#[test]
fn nginx_with_a_site_is_in_use() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");
    host.place_config(
        Path::new("/etc/maran/nginx/sites/example.com.conf"),
        "server {}",
    );

    assert!(matches!(
        uninstall(&host, "nginx"),
        Err(AppsOpError::InUse { .. })
    ));
}

#[test]
fn a_removal_that_leaves_the_app_behind_is_a_failure() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::StillAbsent { .. })
    ));
}

const MARKER: &str = "/etc/maran/site-web-server";
const PORT80: &str = "/etc/nginx/conf.d/maran-port80.conf";
const PORT80_PARKED: &str = "/etc/nginx/conf.d/maran-port80.conf.suspended";
const PORT80_BLOCK: &str = "server { listen 80; server_name panel.example.com; }\n";

fn host_with_apache_that_parked_nginx_port_80() -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new(MARKER), "apache\n");
    host.place_config(Path::new(PORT80_PARKED), PORT80_BLOCK);
    host.removes_files_on_start(&["/usr/sbin/apache2ctl"]);
    host
}

#[test]
fn removing_apache_gives_nginx_its_port_80_block_back_and_clears_the_choice() {
    let host = host_with_apache_that_parked_nginx_port_80();

    uninstall(&host, "apache").unwrap();

    assert_eq!(
        host.config(Path::new(PORT80)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
    assert!(host.config(Path::new(MARKER)).is_none());
    // nginx was validated and reloaded for it.
    let protocol = host.protocol_commands();
    assert!(
        protocol
            .iter()
            .any(|call| call[0].ends_with("nginx") && call[1] == "-t")
    );
    assert!(
        protocol
            .iter()
            .any(|call| call[1] == "reload" && call.last().map(String::as_str) == Some("nginx"))
    );
}

#[test]
fn a_refusal_by_nginx_keeps_the_parked_block_and_the_choice_so_a_retry_converges() {
    let host = host_with_apache_that_parked_nginx_port_80();
    host.reject_validation("nginx: configuration file test failed");
    host.limit_validation_refusal_to("nginx");

    assert!(uninstall(&host, "apache").is_err());

    assert!(host.config(Path::new(PORT80)).is_none());
    assert_eq!(
        host.config(Path::new(PORT80_PARKED)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("apache\n"));
}

#[test]
fn an_app_that_stays_installed_leaves_the_parked_block_where_it_is() {
    // Inverse control of the restore: a removal that failed to remove Apache must not give port 80
    // to nginx while Apache still holds it.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new(MARKER), "apache\n");
    host.place_config(Path::new(PORT80_PARKED), PORT80_BLOCK);

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::StillAbsent { .. })
    ));
    assert!(host.config(Path::new(PORT80)).is_none());
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("apache\n"));
}
