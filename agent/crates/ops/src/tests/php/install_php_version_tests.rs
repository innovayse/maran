//! Tests for [`install_php_version`].
//!
//! The package manager is not run by the agent: the operation starts the
//! installer's `maran-php-install@<package>` unit through the service manager
//! and waits for it. These tests pin the argv it hands the service manager,
//! that nothing else ever stands in for that unit, and what a failure carries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::supported_versions::SUPPORTED_VERSIONS;
use crate::php::{PhpOpError, install_php_version};

/// Runs the installation against `adapter` and returns its outcome with the
/// `(percent, stage)` pairs it reported before ending.
fn install_reporting(
    host: &FakePhpHost,
    adapter: &dyn DistroAdapter,
    version: &str,
) -> (Result<(), PhpOpError>, Vec<(u32, String)>) {
    let reported = Mutex::new(Vec::new());
    let result = install_php_version(
        host,
        adapter,
        &PhpVersion::parse(version).unwrap(),
        |percent, stage| {
            reported.lock().unwrap().push((percent, stage.to_owned()));
        },
    );

    (result, reported.into_inner().unwrap())
}

/// Runs the installation against the default adapter and returns the progress
/// it reported, or the error it ended with.
fn install(host: &FakePhpHost, version: &str) -> Result<Vec<(u32, String)>, PhpOpError> {
    let (result, progress) = install_reporting(host, distro(), version);

    result.map(|()| progress)
}

/// The install unit's name for `package`, spelled out rather than built by the
/// code under test.
fn unit_for(package: &str) -> String {
    format!("maran-php-install@{package}.service")
}

/// Whether any recorded command ran `program`.
fn ran_program(host: &FakePhpHost, program: &str) -> bool {
    host.commands()
        .iter()
        .any(|command| command.first().is_some_and(|first| first == program))
}

#[test]
fn an_unsupported_version_never_reaches_the_install_unit() {
    // The closed set (spec §11) enforced where it matters: the unit installs
    // whatever package it is named after, so if `9.9` reached it the caller —
    // not the agent — would be choosing what root installs on the machine.
    let host = FakePhpHost::empty();

    match install(&host, "9.9") {
        Err(PhpOpError::UnsupportedVersion { version }) => assert_eq!(version, "9.9"),
        other => panic!("expected UnsupportedVersion, got {other:?}"),
    }
    assert!(host.commands().is_empty());
}

#[test]
fn a_version_already_installed_completes_immediately_at_one_hundred_percent() {
    // The retry path. The panel retries after a timeout, and a retry that ran
    // the install unit again would take the package database lock and stall
    // for minutes to achieve nothing.
    let host = FakePhpHost::with_installed(&["8.3"]);

    let progress = install(&host, "8.3").unwrap();

    assert!(host.commands().is_empty());
    assert_eq!(progress.last().unwrap().0, 100);
}

#[test]
fn installing_starts_the_install_unit_named_after_the_adapters_package_as_an_argv_array() {
    // One argv array and no shell. The unit's instance is the adapter's
    // package name, so the agent names no package spelling of its own, and
    // `start` without `--no-block` is what makes the exit status the verdict.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    install(&host, "8.3").unwrap();

    assert_eq!(
        host.commands()[0],
        vec![
            distro().service_manager().to_string(),
            "start".to_owned(),
            "--".to_owned(),
            unit_for(&distro().php_package("8.3")),
        ]
    );
    assert_eq!(
        host.commands()[0][3],
        "maran-php-install@php8.3-fpm.service"
    );
}

#[test]
fn the_install_unit_name_follows_the_rhel_adapter_too() {
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");
    let rhel = adapter_for(DistroFamily::Rhel);

    let (result, _) = install_reporting(&host, rhel, "8.3");

    result.unwrap();
    assert_eq!(
        host.commands()[0],
        vec![
            rhel.service_manager().to_string(),
            "start".to_owned(),
            "--".to_owned(),
            "maran-php-install@php83-php-fpm.service".to_owned(),
        ]
    );
}

#[test]
fn installing_never_runs_the_package_manager_inside_the_agent() {
    // The agent's own sandbox keeps /usr read-only, so a package manager
    // spawned from it fails on the first file. Both families, and both a run
    // that succeeds and one that fails.
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        let adapter = adapter_for(family);

        let succeeding = FakePhpHost::empty();
        succeeding.installs_on_start("8.3");
        install_reporting(&succeeding, adapter, "8.3").0.unwrap();
        assert!(!ran_program(&succeeding, adapter.package_manager()));

        let failing = FakePhpHost::empty();
        failing.answer("start", 1, "", "Job failed");
        assert!(install_reporting(&failing, adapter, "8.3").0.is_err());
        assert!(!ran_program(&failing, adapter.package_manager()));
    }
}

#[test]
fn a_failed_install_unit_never_falls_back_to_the_package_manager() {
    // No fallback: a host whose installer was not re-run has no unit, and the
    // answer is to say so, not to run the package manager here.
    let host = FakePhpHost::empty();
    host.answer(
        "start",
        5,
        "",
        "Unit maran-php-install@php8.3-fpm.service not found.",
    );

    let (result, _) = install_reporting(&host, distro(), "8.3");

    assert!(result.is_err());
    assert_eq!(host.commands().len(), 1);
    assert!(!ran_program(&host, distro().package_manager()));
}

#[test]
fn a_failed_install_unit_is_a_package_manager_failure_carrying_its_report() {
    let host = FakePhpHost::empty();
    host.answer(
        "start",
        1,
        "",
        "Job for maran-php-install@php8.3-fpm.service failed because the control process exited with error code.",
    );

    match install(&host, "8.3") {
        Err(PhpOpError::PackageManager { stderr }) => {
            assert!(stderr.contains("Job for maran-php-install@php8.3-fpm.service failed"));
            assert!(stderr.contains("journalctl -u maran-php-install@php8.3-fpm.service"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_missing_install_unit_is_reported_as_needing_the_installer_rerun() {
    let host = FakePhpHost::empty();
    host.answer(
        "start",
        5,
        "",
        "Unit maran-php-install@php8.3-fpm.service not found.",
    );

    match install(&host, "8.3") {
        Err(PhpOpError::PackageManager { stderr }) => {
            assert!(stderr.contains("installer"));
            assert!(stderr.contains("maran-php-install@php8.3-fpm.service"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_start_that_cannot_be_spawned_is_reported_with_the_unit_and_the_reason() {
    let host = FakePhpHost::empty();
    host.fail_to_spawn("start");

    match install(&host, "8.3") {
        Err(PhpOpError::PackageManager { stderr }) => {
            assert!(stderr.contains("no such file or directory"));
            assert!(stderr.contains("maran-php-install@php8.3-fpm.service"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
    assert!(!ran_program(&host, distro().package_manager()));
}

#[test]
fn a_unit_that_succeeds_without_creating_the_pool_directory_is_not_reported_installed() {
    // Success is checked, not assumed: a package manager that matched some
    // other package by pattern exits 0 and leaves no pool directory behind.
    let host = FakePhpHost::empty();

    let (result, progress) = install_reporting(&host, distro(), "8.3");

    match result {
        Err(PhpOpError::PackageManager { stderr }) => {
            assert!(stderr.contains("still not installed"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
    assert!(
        host.commands()
            .iter()
            .all(|command| !command.contains(&"enable".to_owned()))
    );
    assert!(progress.iter().all(|(percent, _)| *percent < 80));
}

#[test]
fn every_instance_the_agent_can_produce_is_a_plain_unit_name() {
    // The instance reaches the service manager as one argv element and the
    // unit hands it to the package manager as the whole package argument, so
    // it must not be able to become an option, a path, a pin or a removal.
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        let adapter = adapter_for(family);
        for version in SUPPORTED_VERSIONS {
            let host = FakePhpHost::empty();
            host.installs_on_start(version);
            install_reporting(&host, adapter, version).0.unwrap();

            let unit = host.commands()[0][3].clone();
            let instance = unit
                .strip_prefix("maran-php-install@")
                .and_then(|rest| rest.strip_suffix(".service"))
                .unwrap();

            assert!(
                instance.starts_with(|c: char| c.is_ascii_lowercase() || c.is_ascii_digit()),
                "{instance} must not start like an option"
            );
            assert!(
                instance
                    .chars()
                    .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-' || c == '.'),
                "{instance} holds a character a unit name would need escaped"
            );
        }
    }
}

#[test]
fn the_service_is_enabled_and_started_after_the_package_lands() {
    // A version installed but not running is a pool whose socket never
    // appears, which surfaces as a 502 on the first site pointed at it rather
    // than as a failure of this operation.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    install(&host, "8.3").unwrap();

    assert_eq!(
        host.commands()[1],
        vec![
            distro().service_manager().to_string(),
            "enable".to_owned(),
            "--now".to_owned(),
            "--".to_owned(),
            distro().php_fpm_service("8.3"),
        ]
    );
}

#[test]
fn an_enable_refusal_after_a_successful_install_is_a_service_enable_failure() {
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");
    host.answer("enable", 1, "", "Failed to enable unit");

    match install(&host, "8.3") {
        Err(PhpOpError::ServiceEnable { stderr }) => assert!(stderr.contains("Failed to enable")),
        other => panic!("expected ServiceEnable, got {other:?}"),
    }
}

#[test]
fn an_enable_that_cannot_be_spawned_is_a_service_enable_failure_with_the_reason() {
    // The service manager failing to START (not answering non-zero) at the
    // enable step is still the enable's failure, not the package install's.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");
    host.fail_to_spawn("enable");

    match install(&host, "8.3") {
        Err(PhpOpError::ServiceEnable { stderr }) => {
            assert!(stderr.contains("no such file or directory"));
        }
        other => panic!("expected ServiceEnable, got {other:?}"),
    }
}

#[test]
fn the_stages_are_preparing_download_install_enable_and_never_go_backwards() {
    // The panel draws one bar for the whole operation: a bar that starts at
    // 40% or never reaches 100% reads as a bug in the panel.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    let progress = install(&host, "8.3").unwrap();

    assert_eq!(
        progress,
        vec![
            (10, "preparing".to_owned()),
            (30, "download".to_owned()),
            (80, "install".to_owned()),
            (100, "enable".to_owned()),
        ]
    );
}

#[test]
fn installing_writes_no_file_and_creates_no_directory() {
    // Nothing the unit reads is written by the agent: the unit takes its
    // whole input from its own name.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    install(&host, "8.3").unwrap();

    assert_eq!(host.writes(), 0);
    assert_eq!(host.removals(), 0);
    assert!(host.created_as_account().is_empty());
    // Not just the pool directory: no directory at ANY path.
    assert!(host.created_directories().is_empty());
    assert_eq!(
        host.mode(std::path::Path::new(
            &distro().php_fpm_pool_directory("8.3")
        )),
        None
    );
}

#[test]
fn a_package_manager_refusal_is_reported_with_its_output() {
    let host = FakePhpHost::empty();
    host.reject_commands("E: Unable to locate package php8.3-fpm");

    match install(&host, "8.3") {
        Err(PhpOpError::PackageManager { stderr }) => assert!(stderr.contains("Unable to locate")),
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_package_install_that_times_out_is_a_bound_not_a_package_manager_failure() {
    // The install unit lives in the service manager and keeps running after the agent
    // stops waiting, so the version may still appear. `PackageManager` would say it
    // did not install, which is exactly what a killed client cannot know.
    let host = FakePhpHost::empty();
    host.time_out("start");

    match install(&host, "8.3") {
        Err(PhpOpError::Bound(bound)) => {
            assert!(matches!(bound, BoundExceeded::TimedOut { .. }), "{bound:?}");
        }
        other => panic!("expected Bound, got {other:?}"),
    }
    assert_eq!(host.commands().len(), 1, "the enable step is never reached");
}

#[test]
fn an_enable_that_times_out_is_a_bound_not_a_service_enable_failure() {
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");
    host.time_out("enable");

    match install(&host, "8.3") {
        Err(PhpOpError::Bound(_)) => {}
        other => panic!("expected Bound, got {other:?}"),
    }
}

#[test]
fn installing_php_runs_the_package_manager_in_the_package_install_class() {
    // `PackageInstall` is the one class sized for a blocking oneshot unit of up to 1800 s.
    // The enable that follows is a service-manager change and runs as a reload.
    let host = FakePhpHost::empty();
    host.installs_on_start("8.3");

    install(&host, "8.3").unwrap();

    assert_eq!(
        host.classes(),
        vec![CallClass::PackageInstall, CallClass::Reload]
    );
}
