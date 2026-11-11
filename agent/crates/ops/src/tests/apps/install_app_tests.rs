//! Tests for [`install_app`]: the unit started, the order of the steps, the refusals.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroFamily, adapter_for};

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

/// The service-manager subcommands the host was asked to run, in order, as `subcommand unit`.
fn service_calls(host: &FakePhpHost, subcommand: &str) -> Vec<usize> {
    host.commands()
        .iter()
        .enumerate()
        .filter(|(_, call)| call.get(1).map(String::as_str) == Some(subcommand))
        .map(|(index, _)| index)
        .collect()
}

#[test]
fn apache_is_held_off_port_80_before_its_package_is_installed_and_released_after() {
    // Issue #529: the Debian package starts apache2 in its postinst, which cannot bind port 80
    // while nginx holds it. The unit is masked and nginx's block parked BEFORE the install unit.
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);

    install(&host, "apache").0.unwrap();

    let mask = service_calls(&host, "mask");
    let start = service_calls(&host, "start");
    let unmask = service_calls(&host, "unmask");
    let enable = service_calls(&host, "enable");
    assert_eq!((mask.len(), unmask.len()), (1, 1));
    assert!(
        mask[0] < start[0],
        "the unit was masked after the install began"
    );
    assert!(start[0] < unmask[0] && unmask[0] < enable[0]);
    assert_eq!(host.commands()[mask[0]][2..], ["--", "apache2"]);
    // And the block ends parked, for Apache to take the port.
    assert!(host.config(Path::new(PORT80)).is_none());
    assert!(host.config(Path::new(PORT80_PARKED)).is_some());
}

#[test]
fn nothing_is_installed_while_nginx_cannot_release_port_80() {
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);
    host.reject_validation("nginx: configuration file test failed");
    host.limit_validation_refusal_to("nginx");

    let (outcome, _) = install(&host, "apache");

    assert!(outcome.is_err());
    // The package was never installed, so no postinst could have been left half done.
    assert!(started_units(&host).is_empty());
    assert!(service_calls(&host, "mask").is_empty());
    assert!(host.config(Path::new("/usr/sbin/apache2ctl")).is_none());
}

#[test]
fn a_failed_package_install_unmasks_the_unit_and_gives_port_80_back_to_nginx() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new(PORT80), PORT80_BLOCK);
    host.answer("start", 1, "", "Job failed");
    host.answer("show", 1, "", "");

    let (outcome, _) = install(&host, "apache");

    assert!(matches!(outcome, Err(AppsOpError::Package(_))));
    assert_eq!(service_calls(&host, "mask").len(), 1);
    assert_eq!(service_calls(&host, "unmask").len(), 1);
    assert_eq!(
        host.config(Path::new(PORT80)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
    assert!(host.config(Path::new(MARKER)).is_none());
}

#[test]
fn a_family_whose_package_does_not_start_apache_is_not_held_off() {
    // Inverse control: the RHEL family's package leaves the service stopped, so there is nothing
    // to mask before the install.
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&[adapter.apache_binary().as_str()]);

    install_app(&host, adapter, &id("apache"), None, |_, _| {}).unwrap();

    assert!(service_calls(&host, "mask").is_empty());
    assert!(service_calls(&host, "unmask").is_empty());
    assert_eq!(
        started_units(&host),
        vec![
            "maran-app-install@httpd.service",
            "maran-app-install@mod_ssl.service"
        ]
    );
}

#[test]
fn an_apache_package_that_is_already_there_is_not_masked_again() {
    // The retry path skips the install, so there is no postinst to hold off.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");

    install(&host, "apache").0.unwrap();

    assert!(service_calls(&host, "mask").is_empty());
}

/// Runs the Apache install with `progress` panicking at the download stage, as a crash inside the
/// package step would, and returns what the unwinding left behind.
fn apache_install_that_panics(host: &FakePhpHost) {
    let outcome = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        install_app(host, distro(), &id("apache"), None, |_, stage| {
            assert_ne!(stage, "download", "the process died in the package step");
        })
    }));
    assert!(outcome.is_err(), "the panic was swallowed");
}

#[test]
fn a_panic_in_the_package_step_still_unmasks_the_unit_and_gives_port_80_back() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new(PORT80), PORT80_BLOCK);

    apache_install_that_panics(&host);

    assert_eq!(service_calls(&host, "mask").len(), 1);
    assert_eq!(service_calls(&host, "unmask").len(), 1);
    assert_eq!(
        host.config(Path::new(PORT80)).as_deref(),
        Some(PORT80_BLOCK)
    );
    assert!(host.config(Path::new(PORT80_PARKED)).is_none());
}

#[test]
fn port_80_is_given_back_when_the_package_step_ends_and_parked_again_for_the_start() {
    // Three nginx validations: park before the package, restore after it, park again right
    // before the service is started. The block ends parked, for Apache to take the port.
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.place_config(Path::new(PORT80), PORT80_BLOCK);

    install(&host, "apache").0.unwrap();

    let nginx_checks = host
        .protocol_commands()
        .iter()
        .filter(|argv| argv[0].ends_with("/nginx") && argv[1] == "-t")
        .count();
    assert_eq!(nginx_checks, 3, "{:?}", host.protocol_commands());
    assert!(host.config(Path::new(PORT80)).is_none());
    assert!(host.config(Path::new(PORT80_PARKED)).is_some());
}

#[test]
fn a_mask_an_earlier_crashed_run_left_is_removed_before_the_retry_enables_the_unit() {
    // The package is there (the earlier run got that far), the unit is still masked.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.answer("is-enabled", 1, "masked\n", "");

    install(&host, "apache").0.unwrap();

    let unmask = service_calls(&host, "unmask");
    let enable = service_calls(&host, "enable");
    assert_eq!(unmask.len(), 1);
    assert!(unmask[0] < enable[0], "the unit was enabled while masked");
    assert!(service_calls(&host, "mask").is_empty());
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("apache"));
}

#[test]
fn a_unit_that_is_not_masked_is_left_alone_on_a_retry() {
    // Inverse control of the recovery: `is-enabled` says disabled, so nothing is unmasked.
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.answer("is-enabled", 1, "disabled\n", "");

    install(&host, "apache").0.unwrap();

    assert!(service_calls(&host, "unmask").is_empty());
    assert_eq!(service_calls(&host, "is-enabled").len(), 1);
}

#[test]
fn an_unmask_that_failed_after_the_install_is_healed_by_the_next_attempt() {
    // The package installed, then the unmask failed: the install reports it and the unit stays
    // masked. The retry finds the package, sees the mask and removes it.
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&["/usr/sbin/apache2ctl"]);
    host.answer_in_turn("unmask", &[(1, "", "Failed to unmask"), (0, "", "")]);
    // Not masked when the first attempt starts, masked ever after.
    host.answer_in_turn("is-enabled", &[(1, "disabled\n", ""), (1, "masked\n", "")]);

    let (first, _) = install(&host, "apache");
    assert!(matches!(first, Err(AppsOpError::ServiceManager { .. })));
    assert!(service_calls(&host, "enable").is_empty());

    let (second, _) = install(&host, "apache");

    second.unwrap();
    assert_eq!(service_calls(&host, "unmask").len(), 2);
    assert_eq!(service_calls(&host, "enable").len(), 1);
    assert_eq!(
        started_units(&host).len(),
        1,
        "the package was installed once"
    );
}

#[test]
fn a_family_that_never_masks_is_not_asked_whether_the_unit_is_masked() {
    // Inverse control: nothing there could have left a mask.
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::empty();
    host.installs_files_on_start(&[adapter.apache_binary().as_str()]);

    install_app(&host, adapter, &id("apache"), None, |_, _| {}).unwrap();

    assert!(service_calls(&host, "is-enabled").is_empty());
}

#[test]
fn on_suse_an_interpreter_without_its_npm_gets_the_npm_package_alone() {
    // nodejs22 was installed before the install brought npm22 with it.
    let adapter = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v22.3.0\n", "");
    host.installs_files_on_start(&["/usr/bin/npm22"]);

    install_app(&host, adapter, &id("nodejs-22"), None, |_, _| {}).unwrap();

    assert_eq!(
        started_units(&host),
        vec!["maran-app-install@npm22.service"]
    );
}

#[test]
fn on_suse_an_interpreter_with_its_npm_starts_no_unit_and_a_missing_npm_after_the_unit_fails() {
    let adapter = adapter_for(DistroFamily::Suse);
    // Inverse control: both there, nothing to do.
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v22.3.0\n", "");
    host.place_config(Path::new("/usr/bin/npm22"), "");
    install_app(&host, adapter, &id("nodejs-22"), None, |_, _| {}).unwrap();
    assert!(started_units(&host).is_empty());

    // The unit succeeded and the launcher is still not there: a failure, not a success.
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v22.3.0\n", "");
    let outcome = install_app(&host, adapter, &id("nodejs-22"), None, |_, _| {});
    assert!(
        matches!(outcome, Err(AppsOpError::StillAbsent { .. })),
        "{outcome:?}"
    );
}

#[test]
fn on_a_family_without_a_separate_npm_an_interpreter_alone_is_complete() {
    // Inverse control: Debian ships npm inside the nodejs package set of its own choice.
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v18.19.1\n", "");

    install(&host, "nodejs-18").0.unwrap();

    assert!(started_units(&host).is_empty());
}
