//! Tests for how a failed `dnf install` is classified (issue #251).
//!
//! Every output below is what `dnf` printed on `almalinux:9`, recorded on
//! 2026-10-31 into `tests/fixtures/package_manager/`: a repository whose
//! server answered `404` for its metadata, a run with no network at all, one
//! whose mirror list refused the TCP connection, and one asked for a package
//! no configured repository carries. `dnf` exited 1 in all four.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{classify_package_failure, package_manager};
use crate::PackageFailure;

/// `dnf install` against a repository answering its metadata with 404.
const SOURCE_REFUSED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_source_refused.txt");

/// `dnf install` on a host whose resolver answered nothing.
const SOURCE_UNREACHABLE: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_source_unreachable.txt");

/// `dnf install` whose mirror list refused the connection.
const CONNECTION_REFUSED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_source_connection_refused.txt");

/// `dnf install` of a package no configured repository carries.
const PACKAGE_MISSING: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_package_missing.txt");

#[test]
fn a_repository_answering_with_an_http_error_is_a_refused_source() {
    assert_eq!(
        classify_package_failure(1, SOURCE_REFUSED),
        PackageFailure::SourceRefused
    );
}

#[test]
fn a_mirror_that_does_not_resolve_is_an_unreachable_source() {
    assert_eq!(
        classify_package_failure(1, SOURCE_UNREACHABLE),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_connection_the_mirror_refused_is_an_unreachable_source_and_not_a_refusal() {
    // A refused TCP connection is a source nobody reached: nothing answered
    // the request, so it is not the HTTP refusal the other verdict means.
    assert_eq!(
        classify_package_failure(1, CONNECTION_REFUSED),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_package_no_repository_carries_is_a_local_failure() {
    assert_eq!(
        classify_package_failure(1, PACKAGE_MISSING),
        PackageFailure::Local
    );
}

#[test]
fn a_refusal_outweighs_an_unreachable_source_in_the_same_run() {
    let mixed = format!("{SOURCE_UNREACHABLE}{SOURCE_REFUSED}");

    assert_eq!(
        classify_package_failure(1, &mixed),
        PackageFailure::SourceRefused
    );
}

#[test]
fn output_is_not_read_as_dnfs_when_dnf_did_not_exit_with_its_failure_status() {
    assert_eq!(
        classify_package_failure(100, SOURCE_REFUSED),
        PackageFailure::Local
    );
    assert_eq!(
        classify_package_failure(0, SOURCE_UNREACHABLE),
        PackageFailure::Local
    );
}

#[test]
fn a_status_code_line_without_a_three_digit_status_is_not_a_refusal() {
    // The inverse control for the refusal verdict.
    let output = "Error: Status code: unknown for http://mirror/repodata/repomd.xml\n";

    assert_eq!(classify_package_failure(1, output), PackageFailure::Local);
}

#[test]
fn the_package_manager_is_dnf() {
    assert_eq!(package_manager(), "/usr/bin/dnf");
}
