//! Tests for the two Node.js module unit starters: which unit each names, and that a removal is
//! not an install.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_distro::{DistroFamily, adapter_for};

use super::{run_nodejs_install_unit, run_nodejs_remove_unit};
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro};

fn stream(major: &str) -> NodejsModuleStream {
    NodejsModuleStream::from_major(NodeMajor::parse(major).unwrap())
}

#[test]
fn the_install_starter_starts_the_module_install_unit_of_the_stream() {
    let host = FakePhpHost::empty();

    let unit = run_nodejs_install_unit(&host, distro(), &stream("20")).unwrap();

    assert_eq!(unit, "maran-app-nodejs-install@20.service");
    let calls = host.commands();
    assert_eq!(calls.len(), 1);
    assert_eq!(
        calls[0][1..],
        ["start", "--", "maran-app-nodejs-install@20.service"]
    );
    assert_eq!(host.classes(), [CallClass::PackageInstall]);
}

#[test]
fn the_remove_starter_starts_the_module_removal_unit_and_never_an_install_unit() {
    let host = FakePhpHost::empty();

    let unit = run_nodejs_remove_unit(&host, distro(), &stream("22")).unwrap();

    assert_eq!(unit, "maran-app-nodejs-remove@22.service");
    assert!(
        host.commands()
            .iter()
            .all(|call| !call.iter().any(|part| part.contains("install")))
    );
}

#[test]
fn a_module_unit_that_exits_non_zero_is_a_failure_naming_the_unit() {
    let host = FakePhpHost::empty();
    host.answer("start", 1, "", "Job failed");
    host.answer("show", 1, "", "");

    match run_nodejs_install_unit(&host, distro(), &stream("20")) {
        Err(PackageError::PackageManager { stderr }) => {
            assert!(stderr.contains("maran-app-nodejs-install@20.service"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_module_unit_that_outlives_its_deadline_is_a_bound_and_not_a_failure() {
    let host = FakePhpHost::empty();
    host.time_out("start");

    let outcome = run_nodejs_remove_unit(&host, distro(), &stream("20"));

    assert!(
        matches!(outcome, Err(PackageError::Bound(_))),
        "{outcome:?}"
    );
}

/// `dnf install` of a package no enabled repository carries (recorded on `almalinux:9`).
const DNF_PACKAGE_MISSING: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/dnf_package_missing.txt");

#[test]
fn a_stream_no_source_carries_is_reported_by_its_spec_and_not_by_its_digits() {
    let host = FakePhpHost::empty();
    host.answer("start", 1, "", "Job failed");
    host.answer(
        "show",
        0,
        "InvocationID=0f1e2d3c4b5a69788796a5b4c3d2e1f0\nExecMainStatus=1\n",
        "",
    );
    host.answer("--output=cat", 0, DNF_PACKAGE_MISSING, "");

    match run_nodejs_install_unit(&host, adapter_for(DistroFamily::Rhel), &stream("20")) {
        Err(PackageError::NotAvailable { package, .. }) => {
            assert_eq!(package, "nodejs:20/common");
        }
        other => panic!("expected NotAvailable, got {other:?}"),
    }
}
