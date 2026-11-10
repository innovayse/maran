//! Tests for [`classify_package_unit_failure`] (issue #251).
//!
//! The service manager and the journal are the fake host's; the journal's
//! answer is the package manager's own output, recorded on a real
//! `ubuntu:24.04` and `almalinux:9` into the distro crate's fixtures.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use super::classify_package_unit_failure;
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro};

/// The unit whose failed run is being classified.
const UNIT: &str = "maran-app-install@php8.3-fpm.service";

/// The run id the fake service manager names.
const ID: &str = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";

/// `apt-get install` against a mirror answering every archive with 404.
const APT_SOURCE_REFUSED: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/apt_source_refused.txt");

/// `apt-get install` on a host whose resolver answered nothing.
const APT_SOURCE_UNREACHABLE: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/apt_source_unreachable.txt");

/// `apt-get install` of a package no configured repository carries.
const APT_PACKAGE_MISSING: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/apt_package_missing.txt");

/// `dnf install` against a repository answering its metadata with 404.
const DNF_SOURCE_REFUSED: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/dnf_source_refused.txt");

/// A host whose service manager names the failed run with `status`, and
/// whose journal holds `output` for it.
fn host_with_run(status: i32, output: &str) -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.answer(
        "show",
        0,
        &format!("InvocationID={ID}\nExecMainStatus={status}\n"),
        "",
    );
    host.answer("--output=cat", 0, output, "");
    host
}

/// The package the unit was asked to install.
const PACKAGE: &str = "php8.3-fpm";

/// `dnf install` of a package no enabled repository carries.
const DNF_PACKAGE_MISSING: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/dnf_package_missing.txt");

/// Classifies against the default (Debian) adapter.
fn classify(host: &FakePhpHost) -> PackageError {
    classify_package_unit_failure(host, distro(), UNIT, PACKAGE, "Job failed".to_owned())
}

#[test]
fn a_mirror_that_refused_the_archives_is_a_refused_package_source() {
    let host = host_with_run(100, APT_SOURCE_REFUSED);

    match classify(&host) {
        PackageError::SourceRefused { stderr } => assert_eq!(stderr, "Job failed"),
        other => panic!("expected SourceRefused, got {other:?}"),
    }
}

#[test]
fn a_mirror_nobody_reached_is_an_unreachable_package_source() {
    let host = host_with_run(100, APT_SOURCE_UNREACHABLE);

    match classify(&host) {
        PackageError::SourceUnreachable { stderr } => assert_eq!(stderr, "Job failed"),
        other => panic!("expected SourceUnreachable, got {other:?}"),
    }
}

#[test]
fn a_package_no_source_carries_is_not_available_and_names_the_package() {
    let host = host_with_run(100, APT_PACKAGE_MISSING);

    match classify(&host) {
        PackageError::NotAvailable { package, stderr } => {
            assert_eq!(package, PACKAGE);
            assert_eq!(stderr, "Job failed");
        }
        other => panic!("expected NotAvailable, got {other:?}"),
    }
}

#[test]
fn the_rhel_family_reads_no_match_for_argument_as_not_available() {
    let host = host_with_run(1, DNF_PACKAGE_MISSING);

    match classify_package_unit_failure(
        &host,
        adapter_for(DistroFamily::Rhel),
        UNIT,
        PACKAGE,
        "Job failed".to_owned(),
    ) {
        PackageError::NotAvailable { .. } => {}
        other => panic!("expected NotAvailable, got {other:?}"),
    }
}

/// `zypper install` of a package no repository carries, CONSTRUCTED from the tool's documented
/// messages. Not a recording: no SUSE container exists in this phase, so the recording on a real
/// Leap image is owed and the wording is UNVERIFIED. Confirm with
/// `zypper --non-interactive install --no-recommends nosuchpackage`.
const ZYPPER_PACKAGE_MISSING: &str =
    "Loading repository data...\nReading installed packages...\nPackage 'php8.3-fpm' not found.\n";

#[test]
fn the_suse_family_reads_a_package_not_found_as_not_available() {
    // zypper ends a missing package on status 104; the same words under a status zypper does not
    // use for it (the next test) are the host's own failure.
    let host = host_with_run(104, ZYPPER_PACKAGE_MISSING);

    match classify_package_unit_failure(
        &host,
        adapter_for(DistroFamily::Suse),
        UNIT,
        PACKAGE,
        "Job failed".to_owned(),
    ) {
        PackageError::NotAvailable { package, .. } => assert_eq!(package, PACKAGE),
        other => panic!("expected NotAvailable, got {other:?}"),
    }
}

#[test]
fn the_suse_family_does_not_read_a_package_manager_status_it_does_not_use() {
    // Inverse control: status 1 is not one of zypper's own error statuses, so even the
    // missing-package sentence does not make it "not available".
    let host = host_with_run(1, ZYPPER_PACKAGE_MISSING);

    match classify_package_unit_failure(
        &host,
        adapter_for(DistroFamily::Suse),
        UNIT,
        PACKAGE,
        "Job failed".to_owned(),
    ) {
        PackageError::PackageManager { stderr } => assert_eq!(stderr, "Job failed"),
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_local_package_manager_failure_stays_a_package_manager_failure() {
    // Inverse control: an exit status apt does not use is not read as apt's own
    // output, so even the missing-package text does not make it "not available".
    let host = host_with_run(1, APT_PACKAGE_MISSING);

    match classify(&host) {
        PackageError::PackageManager { stderr } => assert_eq!(stderr, "Job failed"),
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn the_family_s_own_reading_is_the_one_asked() {
    // The RHEL adapter reads dnf's words and dnf's failure status.
    let host = host_with_run(1, DNF_SOURCE_REFUSED);

    match classify_package_unit_failure(
        &host,
        adapter_for(DistroFamily::Rhel),
        UNIT,
        PACKAGE,
        "Job failed".to_owned(),
    ) {
        PackageError::SourceRefused { .. } => {}
        other => panic!("expected SourceRefused, got {other:?}"),
    }
}

#[test]
fn the_run_is_named_to_the_journal_by_its_id_and_read_as_message_text() {
    let host = host_with_run(100, APT_SOURCE_REFUSED);

    classify(&host);

    let commands = host.commands();
    assert_eq!(
        commands[0],
        [
            distro().service_manager().as_str(),
            "show",
            "--property=InvocationID",
            "--property=ExecMainStatus",
            "--",
            UNIT,
        ]
    );
    assert_eq!(
        commands[1],
        [
            distro().journal_binary().as_str(),
            "--output=cat",
            "--no-pager",
            &format!("_SYSTEMD_INVOCATION_ID={ID}"),
        ]
    );
    assert_eq!(host.classes(), [CallClass::Query, CallClass::Query]);
}

#[test]
fn a_unit_that_names_no_run_is_a_package_manager_failure_saying_why() {
    let host = FakePhpHost::empty();
    host.answer("show", 0, "InvocationID=\nExecMainStatus=0\n", "");

    match classify(&host) {
        PackageError::PackageManager { stderr } => {
            assert!(stderr.starts_with("Job failed; the run could not be classified: "));
            assert!(stderr.contains("names no finished run"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
    assert_eq!(host.commands().len(), 1);
}

#[test]
fn a_journal_that_cannot_be_read_is_a_package_manager_failure_saying_why() {
    let host = host_with_run(100, APT_SOURCE_REFUSED);
    host.answer("--output=cat", 1, "", "No journal files were found.");

    match classify(&host) {
        PackageError::PackageManager { stderr } => {
            assert!(stderr.contains("`journalctl` exited 1"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_service_manager_that_cannot_be_asked_is_a_package_manager_failure_saying_why() {
    let host = FakePhpHost::empty();
    host.answer("show", 1, "", "Failed to connect to bus");

    match classify(&host) {
        PackageError::PackageManager { stderr } => {
            assert!(stderr.contains("`systemctl show` exited 1"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_journal_reader_that_outlives_its_deadline_is_a_package_manager_failure_saying_why() {
    let host = host_with_run(100, APT_SOURCE_REFUSED);
    host.time_out("--output=cat");

    match classify(&host) {
        PackageError::PackageManager { stderr } => {
            assert!(stderr.contains("the run could not be classified"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}
