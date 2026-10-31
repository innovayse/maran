//! Tests for how a failed `apt-get install` is classified (issue #251).
//!
//! Every output below is what `apt-get` printed on `ubuntu:24.04`, recorded on
//! 2026-10-31 into `tests/fixtures/package_manager/`: a run with no network at
//! all, a run whose mirror answered every archive with `404`, and a run asked
//! for a package no configured repository carries. `apt-get` exited 100 in all
//! three.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{classify_package_failure, package_manager};
use crate::PackageFailure;

/// `apt-get install` against a mirror answering every archive with 404.
const SOURCE_REFUSED: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_source_refused.txt");

/// `apt-get install` on a host whose resolver answered nothing.
const SOURCE_UNREACHABLE: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_source_unreachable.txt");

/// `apt-get install` of a package no configured repository carries.
const PACKAGE_MISSING: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_package_missing.txt");

#[test]
fn a_mirror_answering_with_an_http_error_is_a_refused_source() {
    assert_eq!(
        classify_package_failure(100, SOURCE_REFUSED),
        PackageFailure::SourceRefused
    );
}

#[test]
fn a_mirror_that_does_not_resolve_is_an_unreachable_source() {
    assert_eq!(
        classify_package_failure(100, SOURCE_UNREACHABLE),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_package_no_repository_carries_is_a_local_failure() {
    // The mirror was reached and answered; the HOST's repository set is what
    // lacks the package, so blaming the mirror would send the operator away
    // from the configuration that has to change.
    assert_eq!(
        classify_package_failure(100, PACKAGE_MISSING),
        PackageFailure::Local
    );
}

#[test]
fn a_refusal_outweighs_an_unreachable_source_in_the_same_run() {
    let mixed = format!("{SOURCE_UNREACHABLE}{SOURCE_REFUSED}");

    assert_eq!(
        classify_package_failure(100, &mixed),
        PackageFailure::SourceRefused
    );
}

#[test]
fn output_is_not_read_as_apts_when_apt_did_not_exit_with_its_failure_status() {
    // A run killed by a signal or ended by the service manager did not reach
    // apt's own error reporting, so whatever it printed is not a verdict.
    assert_eq!(
        classify_package_failure(1, SOURCE_REFUSED),
        PackageFailure::Local
    );
    assert_eq!(
        classify_package_failure(-1, SOURCE_UNREACHABLE),
        PackageFailure::Local
    );
}

#[test]
fn an_empty_output_is_a_local_failure() {
    assert_eq!(classify_package_failure(100, ""), PackageFailure::Local);
}

#[test]
fn a_fetch_failure_for_a_reason_that_names_no_source_is_a_local_failure() {
    // The inverse control for the two source verdicts: the same line shape with
    // a reason that is neither an HTTP status nor a network failure.
    let output =
        "E: Failed to fetch http://archive.ubuntu.com/ubuntu/pool/x.deb  Hash Sum mismatch\n";

    assert_eq!(classify_package_failure(100, output), PackageFailure::Local);
}

#[test]
fn the_package_manager_is_apt_get() {
    assert_eq!(package_manager(), "/usr/bin/apt-get");
}
