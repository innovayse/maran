//! Tests for [`prepare_apache`]: what a freshly installed Apache is given.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_distro::{DistroFamily, adapter_for};

use super::prepare_apache;
use crate::apps::AppsOpError;
use crate::php::PhpOpError;
use crate::php::fake_php_host::{FakePhpHost, distro};

#[test]
fn the_include_directory_is_created_and_the_packaged_configuration_includes_it() {
    let host = FakePhpHost::empty();

    prepare_apache(&host, distro(), None).unwrap();

    assert!(
        host.config(Path::new("/etc/apache2/conf-enabled/maran.conf"))
            .unwrap()
            .contains("/etc/maran/apache/sites/*.conf")
    );
    assert_eq!(host.mode(Path::new("/etc/maran/apache/sites")), Some(0o755));
}

#[test]
fn the_rhel_family_writes_into_conf_d_and_has_no_module_enabler() {
    let host = FakePhpHost::empty();

    prepare_apache(&host, adapter_for(DistroFamily::Rhel), None).unwrap();

    assert!(
        host.config(Path::new("/etc/httpd/conf.d/maran.conf"))
            .is_some()
    );
    assert!(
        host.commands()
            .iter()
            .all(|call| !call[0].ends_with("a2enmod"))
    );
}

#[test]
fn an_include_that_apache_refuses_is_not_left_behind() {
    let host = FakePhpHost::empty();
    host.reject_validation("AH00526: Syntax error");

    let outcome = prepare_apache(&host, distro(), None);

    assert!(matches!(
        outcome,
        Err(AppsOpError::Php(PhpOpError::PoolValidation { .. }))
    ));
    assert!(
        host.config(Path::new("/etc/apache2/conf-enabled/maran.conf"))
            .is_none()
    );
}

#[test]
fn a_module_that_cannot_be_enabled_stops_the_preparation_before_the_service_starts() {
    let host = FakePhpHost::empty();
    host.answer(
        "proxy_fcgi",
        1,
        "",
        "ERROR: Module proxy_fcgi does not exist!",
    );

    assert!(matches!(
        prepare_apache(&host, distro(), None),
        Err(AppsOpError::ServiceManager { .. })
    ));
    assert!(
        host.commands()
            .iter()
            .all(|call| call.get(1).map(String::as_str) != Some("enable"))
    );
}

#[test]
fn a_host_without_a_panel_name_gets_no_challenge_virtual_host() {
    let host = FakePhpHost::empty();

    prepare_apache(&host, distro(), None).unwrap();

    // Inverse control of the test below: the challenge file exists only when there is a name.
    assert!(
        host.config(Path::new("/etc/maran/apache/maran-acme.conf"))
            .is_none()
    );
}

#[test]
fn the_panels_challenge_virtual_host_is_written_for_its_name() {
    let host = FakePhpHost::empty();

    prepare_apache(&host, distro(), Some("panel.example.com")).unwrap();

    let acme = host
        .config(Path::new("/etc/maran/apache/maran-acme.conf"))
        .unwrap();
    assert!(acme.contains("ServerName panel.example.com"));
    assert!(acme.contains("Alias /.well-known/acme-challenge/ /var/lib/maran-acme/"));
}

#[test]
fn a_second_run_writes_nothing_again() {
    let host = FakePhpHost::empty();
    prepare_apache(&host, distro(), Some("panel.example.com")).unwrap();
    let writes = host.writes();

    prepare_apache(&host, distro(), Some("panel.example.com")).unwrap();

    assert_eq!(host.writes(), writes);
}
