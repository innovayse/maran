//! Tests for [`uninstall_app`]: refused while in use, never the panel's own nginx.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroFamily, adapter_for};

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

/// Makes the dry run of a removal print that it would remove exactly `packages` (the Debian
/// family's `apt-get -s remove`, whose first argument is `-s`).
fn plan_removing(host: &FakePhpHost, packages: &[&str]) {
    let lines: Vec<String> = packages
        .iter()
        .map(|package| format!("Remv {package} [1.0]"))
        .collect();
    host.answer("-s", 0, &lines.join("\n"), "");
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
    plan_removing(&host, &["apache2"]);

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
    plan_removing(&host, &["php8.3-fpm"]);
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
    plan_removing(&host, &["apache2"]);

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
    plan_removing(&host, &["apache2"]);
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
    plan_removing(&host, &["apache2"]);

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::StillAbsent { .. })
    ));
    assert!(host.config(Path::new(PORT80)).is_none());
    assert_eq!(host.config(Path::new(MARKER)).as_deref(), Some("apache\n"));
}

fn host_with_apache() -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new(MARKER), "apache\n");
    host.removes_files_on_start(&["/usr/sbin/apache2ctl"]);
    host
}

#[test]
fn a_removal_that_would_take_a_dependent_package_is_refused_and_no_unit_starts() {
    let host = host_with_apache();
    plan_removing(&host, &["apache2", "python3-certbot-apache"]);

    match uninstall(&host, "apache") {
        Err(AppsOpError::RemovalWouldRemoveOthers { app, packages }) => {
            assert_eq!(app, "apache");
            assert_eq!(packages, "python3-certbot-apache");
        }
        other => panic!("expected RemovalWouldRemoveOthers, got {other:?}"),
    }
    assert!(started_units(&host).is_empty());
    // The choice and the binary are untouched.
    assert!(host.config(Path::new(MARKER)).is_some());
}

#[test]
fn a_removal_that_would_take_the_panels_nginx_is_refused() {
    let host = host_with_apache();
    plan_removing(&host, &["apache2", "nginx"]);

    match uninstall(&host, "apache") {
        Err(AppsOpError::RemovalWouldRemoveOthers { packages, .. }) => {
            assert_eq!(packages, "nginx");
        }
        other => panic!("expected RemovalWouldRemoveOthers, got {other:?}"),
    }
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_removal_of_only_the_apps_own_package_is_not_refused() {
    // Inverse control of the two refusals above: the same host with a plan of its own package.
    let host = host_with_apache();
    plan_removing(&host, &["apache2"]);

    uninstall(&host, "apache").unwrap();

    assert_eq!(started_units(&host).len(), 1);
}

#[test]
fn a_dry_run_that_fails_or_prints_no_plan_refuses_the_removal() {
    for (status, stdout) in [
        (100, "E: Unable to locate package"),
        (0, "Reading package lists..."),
    ] {
        let host = host_with_apache();
        host.answer("-s", status, stdout, "");

        assert!(
            matches!(
                uninstall(&host, "apache"),
                Err(AppsOpError::RemovalNotSimulated { .. })
            ),
            "status {status}"
        );
        assert!(started_units(&host).is_empty());
    }
}

#[test]
fn the_dry_run_is_the_package_managers_with_the_package_after_the_end_of_options() {
    let host = host_with_apache();
    plan_removing(&host, &["apache2"]);

    uninstall(&host, "apache").unwrap();

    let dry_runs: Vec<Vec<String>> = host
        .commands()
        .into_iter()
        .filter(|call| call.get(1).map(String::as_str) == Some("-s"))
        .collect();
    assert_eq!(
        dry_runs,
        vec![vec![
            "/usr/bin/apt-get",
            "-s",
            "-q",
            "-o",
            "Debug::NoLocking=true",
            "remove",
            "--",
            "apache2"
        ]]
    );
}

fn unit_path(domain: &str) -> std::path::PathBuf {
    Path::new(distro().systemd_unit_directory()).join(format!("maran-node-{domain}.service"))
}

fn node_unit(major: &str) -> String {
    format!("[Unit]\nDescription=Maran Node.js {major} application for example.com\n")
}

fn host_with_node_20() -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");
    plan_removing(&host, &["nodejs"]);
    host
}

#[test]
fn a_node_major_a_site_unit_still_runs_on_is_in_use_and_nothing_starts() {
    let host = host_with_node_20();
    host.place_config(&unit_path("example.com"), &node_unit("20"));

    assert!(matches!(
        uninstall(&host, "nodejs-20"),
        Err(AppsOpError::InUse { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_site_unit_of_another_major_does_not_keep_the_node_major_and_other_files_are_not_units() {
    // Inverse controls: a unit declaring 22, and files that are not per-site Node.js units.
    let host = host_with_node_20();
    host.place_config(&unit_path("example.com"), &node_unit("22"));
    host.place_config(
        &Path::new(distro().systemd_unit_directory()).join("maran-agent.service"),
        &node_unit("20"),
    );

    let outcome = uninstall(&host, "nodejs-20");

    // The unit starts; the fake's interpreter still answers, which is the later StillAbsent.
    assert!(
        matches!(outcome, Err(AppsOpError::StillAbsent { .. })),
        "{outcome:?}"
    );
    assert_eq!(
        started_units(&host),
        vec!["maran-app-remove@nodejs.service"]
    );
}

#[test]
fn a_site_unit_that_states_no_major_keeps_every_node_major() {
    let host = host_with_node_20();
    host.place_config(
        &unit_path("example.com"),
        "[Unit]\nDescription=hand written\n",
    );

    assert!(matches!(
        uninstall(&host, "nodejs-20"),
        Err(AppsOpError::InUse { .. })
    ));
}

#[test]
fn an_unreadable_unit_directory_fails_closed_and_nothing_starts() {
    // A directory that cannot be listed says nothing about the sites under it.
    let host = host_with_node_20();
    host.make_unreadable(Path::new(distro().systemd_unit_directory()));

    assert!(matches!(
        uninstall(&host, "nodejs-20"),
        Err(AppsOpError::HostUnreadable { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn an_unreadable_vhost_directory_fails_closed_for_a_web_server() {
    let host = host_with_apache();
    plan_removing(&host, &["apache2"]);
    host.make_unreadable(Path::new("/etc/maran/apache/sites"));

    assert!(matches!(
        uninstall(&host, "apache"),
        Err(AppsOpError::HostUnreadable { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_unit_directory_that_does_not_exist_holds_no_site_and_is_not_a_refusal() {
    // Inverse control of the two above: absent is empty, readable-and-empty proceeds.
    let host = host_with_node_20();

    let outcome = uninstall(&host, "nodejs-20");

    assert!(
        matches!(outcome, Err(AppsOpError::StillAbsent { .. })),
        "{outcome:?}"
    );
    assert_eq!(
        started_units(&host),
        vec!["maran-app-remove@nodejs.service"]
    );
}

#[test]
fn npm_that_needs_nodejs_is_not_removed_as_a_side_effect() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");
    plan_removing(&host, &["nodejs", "npm"]);

    assert!(matches!(
        uninstall(&host, "nodejs-20"),
        Err(AppsOpError::RemovalWouldRemoveOthers { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn on_suse_the_npm_package_goes_first_and_both_are_the_apps_own() {
    let adapter = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v22.3.0\n", "");
    let plan = |package: &str| {
        format!(
            "The following package is going to be REMOVED:\n  {package}\n\n1 package to remove.\n"
        )
    };
    host.answer_in_turn(
        "--non-interactive",
        &[(0, &plan("nodejs22 npm22"), ""), (0, &plan("npm22"), "")],
    );

    let outcome = uninstall_app(&host, adapter, &id("nodejs-22"), |_, _| {});

    assert!(
        matches!(outcome, Err(AppsOpError::StillAbsent { .. })),
        "{outcome:?}"
    );
    assert_eq!(
        started_units(&host),
        vec![
            "maran-app-remove@npm22.service",
            "maran-app-remove@nodejs22.service"
        ]
    );
}

/// A recorded `dnf remove --assumeno httpd` (exit 1): `httpd` itself and four unused dependencies.
const RECORDED_DNF_HTTPD: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_remove_simulation_httpd.txt"
);

/// A recorded `zypper --non-interactive remove --dry-run -- apache2`: `apache2` and its MPM.
const RECORDED_ZYPPER_APACHE2: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/zypper_remove_dry_run_apache2.txt"
);

fn removal_on(family: DistroFamily, host: &FakePhpHost) -> Result<(), AppsOpError> {
    uninstall_app(host, adapter_for(family), &id("apache"), |_, _| {})
}

fn host_with_httpd(adapter: &dyn maran_distro::DistroAdapter) -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.place_config(adapter.apache_binary().as_path(), "");
    host.place_config(Path::new(MARKER), "apache\n");
    host.removes_files_on_start(&[adapter.apache_binary().as_str()]);
    host
}

#[test]
fn the_unused_libraries_of_a_recorded_dnf_plan_do_not_refuse_the_removal_of_httpd() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = host_with_httpd(adapter);
    host.answer("remove", 1, RECORDED_DNF_HTTPD, "");

    removal_on(DistroFamily::Rhel, &host).unwrap();

    // `mod_ssl` first, then `httpd`: both are the app's own.
    assert_eq!(
        started_units(&host),
        vec![
            "maran-app-remove@mod_ssl.service",
            "maran-app-remove@httpd.service"
        ]
    );
}

#[test]
fn nginx_among_the_unused_dependencies_of_a_dnf_plan_refuses_the_removal() {
    // Inverse control of the one above: the same plan with the panel's front in the unused list.
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = host_with_httpd(adapter);
    let plan = RECORDED_DNF_HTTPD.replace(" libbrotli ", " nginx ");
    host.answer("remove", 1, &plan, "");

    match removal_on(DistroFamily::Rhel, &host) {
        Err(AppsOpError::RemovalWouldRemoveOthers { packages, .. }) => {
            assert_eq!(packages, "nginx");
        }
        other => panic!("expected RemovalWouldRemoveOthers, got {other:?}"),
    }
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_dnf_plan_with_a_line_the_reader_does_not_know_refuses_the_removal() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = host_with_httpd(adapter);
    let plan = RECORDED_DNF_HTTPD.replace(
        "Removing unused dependencies:",
        "Removing unused dependencies (new layout):",
    );
    host.answer("remove", 1, &plan, "");

    assert!(matches!(
        removal_on(DistroFamily::Rhel, &host),
        Err(AppsOpError::RemovalNotSimulated { .. })
    ));
    assert!(started_units(&host).is_empty());
}

#[test]
fn the_mpm_that_zypper_removes_with_apache2_is_the_apps_own_and_any_other_package_is_not() {
    let adapter = adapter_for(DistroFamily::Suse);
    let host = host_with_httpd(adapter);
    host.answer("--non-interactive", 0, RECORDED_ZYPPER_APACHE2, "");

    removal_on(DistroFamily::Suse, &host).unwrap();
    assert_eq!(
        started_units(&host),
        vec!["maran-app-remove@apache2.service"]
    );

    // Inverse control: a plan that also removes a package that is no MPM is refused.
    let host = host_with_httpd(adapter);
    host.answer(
        "--non-interactive",
        0,
        &RECORDED_ZYPPER_APACHE2
            .replace("apache2-prefork", "apache2-prefork python3-certbot-apache"),
        "",
    );
    match removal_on(DistroFamily::Suse, &host) {
        Err(AppsOpError::RemovalWouldRemoveOthers { packages, .. }) => {
            assert_eq!(packages, "python3-certbot-apache");
        }
        other => panic!("expected RemovalWouldRemoveOthers, got {other:?}"),
    }
    assert!(started_units(&host).is_empty());
}

#[test]
fn a_debian_plan_that_installs_or_upgrades_something_refuses_the_removal() {
    for noise in [
        "Inst libfoo (1.0 Ubuntu:24.04 [amd64])",
        "Conf libfoo (1.0 [amd64])",
    ] {
        let host = host_with_apache();
        host.answer("-s", 0, &format!("Remv apache2 [2.4]\n{noise}"), "");

        assert!(matches!(
            uninstall(&host, "apache"),
            Err(AppsOpError::RemovalNotSimulated { .. })
        ));
        assert!(started_units(&host).is_empty());
    }
}
