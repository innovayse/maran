//! Tests for [`write_opcache_drop_in`].
//!
//! What these pin is the decision the operation makes: write nothing when the
//! file is already current, go through the validate-and-reload protocol when it
//! is not, and refuse a version it may not touch.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroFamily, adapter_for};

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::write_opcache_drop_in::write_opcache_drop_in;
use crate::php::{PhpHost, PhpOpError};

/// The version every test writes for.
fn version() -> PhpVersion {
    PhpVersion::parse("8.3").unwrap()
}

/// Where the default adapter keeps the drop-in of 8.3, spelled by the test from
/// the adapter's directory rather than by the code under test.
fn target() -> PathBuf {
    PathBuf::from(format!(
        "{}/99-maran-opcache.ini",
        distro().php_ini_drop_in_directory(&version())
    ))
}

#[test]
fn a_missing_drop_in_is_written_validated_and_reloaded() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_opcache_drop_in(&host, distro(), &version()).unwrap();

    let written = host.config(&target()).expect("no drop-in was written");
    assert!(
        written.contains("opcache.validate_permission=1"),
        "{written}"
    );
    assert_eq!(host.writes(), 1);
    let protocol = host.protocol_commands();
    assert_eq!(protocol.len(), 2, "{protocol:?}");
    assert_eq!(
        protocol[0][0],
        distro().php_fpm_binary(&version()).to_string()
    );
    assert_eq!(protocol[0][1], "-t");
    assert_eq!(protocol[1][0], distro().service_manager().to_string());
    assert_eq!(protocol[1][1], "reload");
    assert_eq!(
        protocol[1].last().unwrap(),
        &distro().php_fpm_service("8.3")
    );
    assert_eq!(host.protocol_classes(), vec![CallClass::Validate]);
}

#[test]
fn a_drop_in_that_equals_the_render_runs_nothing() {
    // The start-up pass and the install retry path both call this: when the
    // file is current it must cost one read, not a validation and a reload.
    let host = FakePhpHost::with_installed(&["8.3"]);
    write_opcache_drop_in(&host, distro(), &version()).unwrap();

    write_opcache_drop_in(&host, distro(), &version()).unwrap();

    assert_eq!(host.writes(), 1);
    assert_eq!(host.protocol_commands().len(), 2);
    assert!(host.commands().is_empty());
}

#[test]
fn a_drop_in_edited_by_hand_is_replaced() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.place_config(&target(), "opcache.validate_permission=0\n");

    write_opcache_drop_in(&host, distro(), &version()).unwrap();

    let written = host.config(&target()).unwrap();
    assert!(
        written.contains("opcache.validate_permission=1"),
        "{written}"
    );
    assert!(!written.contains("validate_permission=0"), "{written}");
    assert_eq!(host.writes(), 1);
}

#[test]
fn the_rendered_drop_in_carries_each_isolation_line() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_opcache_drop_in(&host, distro(), &version()).unwrap();

    let written = host.config(&target()).unwrap();
    for expected in [
        "opcache.validate_permission=1",
        "opcache.validate_root=1",
        "opcache.restrict_api=/nonexistent-maran-opcache-api",
    ] {
        assert!(written.lines().any(|line| line == expected), "{written}");
    }
}

#[test]
fn an_unsupported_version_is_refused_before_anything_is_read_or_written() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    match write_opcache_drop_in(&host, distro(), &PhpVersion::parse("9.9").unwrap()) {
        Err(PhpOpError::UnsupportedVersion { version }) => assert_eq!(version, "9.9"),
        other => panic!("expected UnsupportedVersion, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_version_that_is_not_installed_is_refused_and_nothing_is_written() {
    let host = FakePhpHost::empty();

    match write_opcache_drop_in(&host, distro(), &version()) {
        Err(PhpOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.3"),
        other => panic!("expected PhpVersionNotInstalled, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_validation_refusal_is_returned_and_the_previous_drop_in_stays() {
    // The real protocol restores the previous bytes before returning; the fake
    // leaves its map untouched on a refusal, which is the same observable.
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.place_config(&target(), "opcache.enable=1\n");
    host.reject_validation("PHP Fatal: bad directive");

    match write_opcache_drop_in(&host, distro(), &version()) {
        Err(PhpOpError::PoolValidation { stderr }) => {
            assert_eq!(stderr, "PHP Fatal: bad directive");
        }
        other => panic!("expected PoolValidation, got {other:?}"),
    }
    assert_eq!(host.config(&target()).unwrap(), "opcache.enable=1\n");
}

#[test]
fn a_deadline_is_a_bound_and_not_a_refusal() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.time_out_protocol();

    assert!(matches!(
        write_opcache_drop_in(&host, distro(), &version()),
        Err(PhpOpError::Bound(_))
    ));
}

#[test]
fn the_rhel_adapter_writes_into_its_own_directory() {
    let rhel = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::with_installed(&[]);
    // The fake knows installed versions by the default adapter's pool directory;
    // make the RHEL one exist too.
    host.create_directory(
        std::path::Path::new(&rhel.php_fpm_pool_directory("8.3")),
        0o755,
    )
    .unwrap();

    write_opcache_drop_in(&host, rhel, &version()).unwrap();

    let expected = PathBuf::from(format!(
        "{}/99-maran-opcache.ini",
        rhel.php_ini_drop_in_directory(&version())
    ));
    assert!(host.config(&expected).is_some());
}
