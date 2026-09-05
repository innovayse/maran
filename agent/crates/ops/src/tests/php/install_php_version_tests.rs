//! Tests for [`install_php_version`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::{PhpOpError, install_php_version};

/// Runs the installation and returns the `(percent, stage)` pairs it reported.
fn install(host: &FakePhpHost, version: &str) -> Result<Vec<(u32, String)>, PhpOpError> {
    let reported = Mutex::new(Vec::new());
    let result = install_php_version(
        host,
        distro(),
        &PhpVersion::parse(version).unwrap(),
        |percent, stage| {
            reported.lock().unwrap().push((percent, stage.to_owned()));
        },
    );

    result.map(|()| reported.into_inner().unwrap())
}

#[test]
fn an_unsupported_version_never_reaches_the_package_manager() {
    // The closed set (spec §11) enforced where it matters. If `9.9` reached
    // `apt-get install php9.9-fpm`, the caller — not the agent — would be
    // choosing what root installs on the machine.
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
    // the package manager again would take the package database lock and
    // stall for minutes to achieve nothing.
    let host = FakePhpHost::with_installed(&["8.3"]);

    let progress = install(&host, "8.3").unwrap();

    assert!(host.commands().is_empty());
    assert_eq!(progress.last().unwrap().0, 100);
}

#[test]
fn installing_spawns_the_package_manager_from_the_adapter_as_an_argv_array() {
    // No shell, and no path literal in `ops`: the binary comes from the
    // adapter and the package name is derived by it (rules/security.md item 3,
    // rules/rust.md "Distro adapter").
    let host = FakePhpHost::empty();

    install(&host, "8.3").unwrap();

    assert_eq!(
        host.commands()[0],
        vec![
            distro().package_manager().to_owned(),
            "install".to_owned(),
            "-y".to_owned(),
            distro().php_package("8.3"),
        ]
    );
}

#[test]
fn the_service_is_enabled_and_started_after_the_package_lands() {
    // A version installed but not running is a pool whose socket never
    // appears, which surfaces as a 502 on the first site pointed at it rather
    // than as a failure of this operation.
    let host = FakePhpHost::empty();

    install(&host, "8.3").unwrap();

    assert_eq!(
        host.commands()[1],
        vec![
            distro().service_manager().to_owned(),
            "enable".to_owned(),
            "--now".to_owned(),
            distro().php_fpm_service("8.3"),
        ]
    );
}

#[test]
fn progress_starts_low_and_ends_at_one_hundred() {
    // The panel draws one bar for the whole operation: a bar that starts at
    // 40% or never reaches 100% reads as a bug in the panel.
    let host = FakePhpHost::empty();

    let progress = install(&host, "8.3").unwrap();

    assert_eq!(progress.first().unwrap(), &(10, "preparing".to_owned()));
    assert_eq!(progress.last().unwrap(), &(100, "enable".to_owned()));
    assert!(progress.windows(2).all(|pair| pair[0].0 <= pair[1].0));
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
