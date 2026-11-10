//! Tests for [`install_app`]: the unit started, the order of the steps, the refusals.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use super::install_app;
use crate::apps::AppsOpError;
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro};

fn id(text: &str) -> ValidatedAppId {
    ValidatedAppId::parse(text).unwrap()
}

/// The units the host was asked to start, in order.
fn started_units(host: &FakePhpHost) -> Vec<String> {
    host.commands()
        .into_iter()
        .filter(|call| call.get(1).map(String::as_str) == Some("start"))
        .filter_map(|call| call.last().cloned())
        .collect()
}

fn install(host: &FakePhpHost, app: &str) -> (Result<(), AppsOpError>, Vec<(u32, String)>) {
    let mut stages = Vec::new();
    let outcome = install_app(
        host,
        distro(),
        &id(app),
        Some("panel.example.com"),
        |percent, stage| {
            stages.push((percent, stage.to_owned()));
        },
    );
    (outcome, stages)
}

#[test]
fn apache_is_installed_through_the_app_unit_of_its_package_and_prepared() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);

    let (outcome, stages) = install(&host, "apache");

    outcome.unwrap();
    assert_eq!(
        started_units(&host),
        vec!["maran-app-install@apache2.service"]
    );
    assert_eq!(stages.last().unwrap().0, 100);
    // The vhost directory is included from the packaged configuration, once.
    let snippet = host
        .config(Path::new("/etc/apache2/conf-enabled/maran.conf"))
        .unwrap();
    assert!(snippet.contains("IncludeOptional /etc/maran/apache/sites/*.conf"));
    assert!(snippet.contains("IncludeOptional /etc/maran/apache/maran-acme.conf"));
    // The panel's own challenge virtual host answers on port 80, for the panel's name only.
    let acme = host
        .config(Path::new("/etc/maran/apache/maran-acme.conf"))
        .unwrap();
    assert!(acme.contains("ServerName panel.example.com"));
    assert!(acme.contains("/var/lib/maran-acme"));
    // And the service is enabled and started, after the include is in place.
    let commands = host.commands();
    let enable = commands
        .iter()
        .position(|call| call.get(1).map(String::as_str) == Some("enable"))
        .unwrap();
    assert_eq!(commands[enable][2..], ["--now", "--", "apache2"]);
}

#[test]
fn the_debian_family_enables_the_modules_the_vhost_templates_use() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);

    install(&host, "apache").0.unwrap();

    let enabled: Vec<String> = host
        .commands()
        .into_iter()
        .filter(|call| call[0] == "/usr/sbin/a2enmod")
        .map(|call| call[1].clone())
        .collect();
    assert!(enabled.contains(&"proxy_fcgi".to_owned()));
    assert!(enabled.contains(&"ssl".to_owned()));
}

#[test]
fn an_app_that_is_already_installed_starts_no_install_unit() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");

    let (outcome, stages) = install(&host, "apache");

    outcome.unwrap();
    assert!(started_units(&host).is_empty());
    assert_eq!(stages, vec![(100, "install".to_owned())]);
}

#[test]
fn a_retried_apache_install_writes_the_include_only_when_it_differs() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");

    install(&host, "apache").0.unwrap();
    let writes = host.writes();
    install(&host, "apache").0.unwrap();

    assert_eq!(writes, 2);
    assert_eq!(host.writes(), 2);
}

#[test]
fn a_php_version_is_installed_by_the_php_install_and_through_the_same_unit_name() {
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    install(&host, "php-8.3").0.unwrap();

    assert_eq!(
        started_units(&host)[0],
        "maran-app-install@php8.3-fpm.service"
    );
}

#[test]
fn a_php_version_outside_the_supported_set_is_refused_before_any_unit_starts() {
    let host = FakePhpHost::empty();

    let (outcome, _) = install(&host, "php-9.9");

    assert!(matches!(outcome, Err(AppsOpError::UnsupportedApp { .. })));
    assert!(host.commands().is_empty());
}

#[test]
fn nodejs_is_installed_through_its_package_and_checked_by_the_interpreter() {
    let host = FakePhpHost::empty();
    // Absent when `is_installed` asks, absent when the install asks again before it starts the
    // unit, present when the check after the unit asks.
    host.answer_in_turn(
        "--version",
        &[(1, "", ""), (1, "", ""), (0, "v18.19.1\n", "")],
    );

    let (outcome, _) = install(&host, "nodejs-18");

    outcome.unwrap();
    assert_eq!(
        started_units(&host),
        vec!["maran-app-install@nodejs.service"]
    );
}

#[test]
fn a_unit_that_succeeds_without_the_app_appearing_is_a_failure_naming_the_journal() {
    let host = FakePhpHost::empty();

    let (outcome, _) = install(&host, "apache");

    match outcome {
        Err(AppsOpError::StillAbsent { app, detail }) => {
            assert_eq!(app, "apache");
            assert!(detail.contains("journalctl -u maran-app-install@apache2.service"));
        }
        other => panic!("expected StillAbsent, got {other:?}"),
    }
}

#[test]
fn a_package_source_that_refused_is_reported_as_a_refusal() {
    let host = FakePhpHost::empty();
    host.answer("start", 1, "", "Job failed");
    host.answer("show", 1, "", "");

    let (outcome, _) = install(&host, "apache");

    // The run could not be read back, so it stays this host's failure; the classification
    // itself is pinned in `packages`.
    assert!(matches!(
        outcome,
        Err(AppsOpError::Package(PackageError::PackageManager { .. }))
    ));
}

#[test]
fn nginx_is_refused_while_apache_is_installed() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");

    let (outcome, _) = install(&host, "nginx");

    match outcome {
        Err(AppsOpError::WebServerAlreadyInstalled {
            requested,
            installed,
        }) => {
            assert_eq!(requested, "nginx");
            assert_eq!(installed, "apache");
        }
        other => panic!("expected WebServerAlreadyInstalled, got {other:?}"),
    }
    assert!(started_units(&host).is_empty());
}

#[test]
fn apache_is_refused_while_nginx_serves_sites_and_allowed_when_it_serves_none() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(
        Path::new("/etc/maran/nginx/sites/example.com.conf"),
        "server {}",
    );

    let (refused, _) = install(&host, "apache");
    assert!(matches!(
        refused,
        Err(AppsOpError::WebServerAlreadyInstalled { .. })
    ));

    // Inverse control: the same host with no site on nginx installs Apache.
    let free = FakePhpHost::empty();
    free.place_config(Path::new("/usr/sbin/nginx"), "");
    free.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    install(&free, "apache").0.unwrap();
}

const MARKER: &str = "/etc/maran/site-web-server";
const PORT80: &str = "/etc/nginx/conf.d/maran-port80.conf";
const PORT80_PARKED: &str = "/etc/nginx/conf.d/maran-port80.conf.suspended";
const PORT80_BLOCK: &str = "server { listen 80; server_name panel.example.com; }\n";

#[test]
fn installing_apache_takes_nginx_off_port_80_and_records_the_choice() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);

    install(&host, "apache").0.unwrap();

    // The block is out of nginx's tree and parked byte for byte, so removing Apache can restore it.
    assert!(host.config(Path::new(PORT80)).is_none());
    assert_eq!(
        host.config(Path::new(PORT80_PARKED)).as_deref(),
        Some(PORT80_BLOCK)
    );
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
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("apache"));
}

#[test]
fn apache_is_never_started_while_nginx_still_holds_port_80() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);
    // nginx refuses the tree without the block.
    host.reject_validation("nginx: configuration file test failed");
    host.limit_validation_refusal_to("nginx");

    let (outcome, _) = install(&host, "apache");

    assert!(outcome.is_err());
    assert!(
        host.commands()
            .iter()
            .all(|call| call.get(1).map(String::as_str) != Some("enable")),
        "Apache was started although nginx kept port 80"
    );
    // The block is still live, no parked copy contradicts it and nothing was recorded.
    assert_eq!(
        host.config(Path::new(PORT80)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
    assert!(host.config(Path::new(MARKER)).is_none());
}

#[test]
fn an_apache_that_does_not_start_gives_port_80_back_to_nginx_and_records_nothing() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);
    host.answer("enable", 1, "", "Job for apache2.service failed");

    let (outcome, _) = install(&host, "apache");

    assert!(matches!(outcome, Err(AppsOpError::ServiceManager { .. })));
    assert_eq!(
        host.config(Path::new(PORT80)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
    assert!(host.config(Path::new(MARKER)).is_none());
}

#[test]
fn a_host_without_the_port_80_block_installs_apache_without_touching_nginx() {
    // Inverse control of the three above: nothing parked, no nginx validation or reload.
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);

    install(&host, "apache").0.unwrap();

    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
    assert!(
        host.protocol_commands()
            .iter()
            .all(|call| !call[0].ends_with("nginx"))
    );
}

#[test]
fn installing_nginx_records_the_choice_and_installs_no_package_it_already_has() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");

    let (outcome, stages) = install(&host, "nginx");

    outcome.unwrap();
    assert!(started_units(&host).is_empty());
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("nginx"));
    assert_eq!(stages.last().unwrap().0, 100);
}

#[test]
fn nginx_present_but_not_chosen_is_installed_by_writing_the_marker_only() {
    // Fresh-host control: before the install the marker is absent although the binary is there.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    assert!(host.config(Path::new(MARKER)).is_none());

    install(&host, "nginx").0.unwrap();

    assert!(host.config(Path::new(MARKER)).is_some());
}

#[test]
fn apache_is_refused_while_nginx_is_the_chosen_site_web_server() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new(MARKER), "nginx\n");

    let (outcome, _) = install(&host, "apache");

    assert!(matches!(
        outcome,
        Err(AppsOpError::WebServerAlreadyInstalled { .. })
    ));
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
}
