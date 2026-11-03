//! Tests for how a failed `dnf install` is classified (issue #251).
//!
//! Every output below is what `dnf` printed on `almalinux:9`, recorded on
//! 2026-10-31 into `tests/fixtures/package_manager/`: a repository whose
//! server answered `404` for its metadata, a run with no network at all, one
//! whose mirror list refused the TCP connection, and one asked for a package
//! no configured repository carries. `dnf` exited 1 in all four.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{
    PENDING_UPDATES_ARGUMENTS, classify_package_failure, installable_php_versions, package_manager,
    read_pending_updates,
};
use crate::{PackageFailure, PendingUpdate, PendingUpdatesFailure};

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

#[test]
fn the_installable_php_versions_run_from_seven_four_to_eight_four_oldest_first() {
    assert_eq!(
        installable_php_versions(),
        &["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"]
    );
}

// The pending-updates tests read what `dnf check-update -C -q` printed in
// `almalinux:9.3` containers, recorded on 2026-11-03: after `dnf makecache`
// (updates, exit 100), after `dnf upgrade` (nothing, exit 0), and with no cache
// (exit 1, a message on stderr). The two files that carry a `+` name, a name
// long enough to widen the column, and an `Obsoleting Packages` section were
// recorded from the same image with two throw-away local repositories, because
// no real repository on the image offers those shapes; that run restricted dnf
// to the local repository, which changes which rows are printed and nothing
// about how a row is laid out.

/// `dnf check-update -C -q` after the cache was built on an old image: exit 100.
const WITH_UPDATES: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_check_update_with_updates.txt");

/// The same command after the upgrade was applied: exit 0, no output.
const UP_TO_DATE: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_check_update_up_to_date.txt");

/// What dnf printed, on stderr, with no cache: exit 1 and nothing on stdout.
const NO_CACHE_MESSAGE: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_check_update_no_cache.txt");

/// A `+` and a `.` in a name, and a name long enough to widen the column: exit 100.
const LONG_AND_PLUS_NAMES: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_check_update_long_and_plus_names.txt"
);

/// An `Obsoleting Packages` section with its indented replaced package: exit 100.
const OBSOLETING: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_check_update_obsoleting.txt");

/// The update named `name` in a read, or a panic naming the package.
fn row<'a>(updates: &'a [PendingUpdate], name: &str) -> &'a PendingUpdate {
    updates
        .iter()
        .find(|update| update.package_name == name)
        .unwrap_or_else(|| panic!("no row for {name}"))
}

#[test]
fn the_pending_updates_run_is_cache_only_with_no_operand() {
    assert_eq!(PENDING_UPDATES_ARGUMENTS, &["check-update", "-C", "-q"]);
}

#[test]
fn every_row_of_an_answer_that_said_updates_exist_is_an_update() {
    let updates = read_pending_updates(100, WITH_UPDATES).unwrap();

    assert_eq!(updates.len(), 115);
}

#[test]
fn a_row_carries_the_new_version_the_repository_and_no_installed_version() {
    let updates = read_pending_updates(100, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "acl"),
        &PendingUpdate {
            package_name: String::from("acl"),
            available_version: String::from("2.4.0-1.el9_8"),
            installed_version: None,
            source: String::from("baseos"),
        }
    );
}

#[test]
fn an_epoch_stays_part_of_the_version() {
    let updates = read_pending_updates(100, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "shadow-utils").available_version,
        "2:4.9-16.el9"
    );
}

#[test]
fn the_architecture_suffix_is_not_part_of_the_name() {
    let updates = read_pending_updates(100, WITH_UPDATES).unwrap();

    assert!(
        updates
            .iter()
            .all(|update| !update.package_name.ends_with(".x86_64"))
    );
    assert!(
        updates
            .iter()
            .all(|update| !update.package_name.ends_with(".noarch"))
    );
}

#[test]
fn a_name_with_a_plus_and_a_dot_keeps_both() {
    let updates = read_pending_updates(100, LONG_AND_PLUS_NAMES).unwrap();

    assert_eq!(
        row(&updates, "maran-fixture-c++.lib").available_version,
        "2.0-1"
    );
}

#[test]
fn a_name_long_enough_to_widen_the_column_is_still_one_row() {
    let updates = read_pending_updates(100, LONG_AND_PLUS_NAMES).unwrap();

    assert_eq!(updates.len(), 2);
    assert_eq!(
        row(
            &updates,
            "maran-fixture-package-with-an-exceptionally-long-name-to-force-wrapping"
        )
        .source,
        "fixture-two"
    );
}

#[test]
fn an_obsoleting_package_is_a_row_and_the_package_it_replaces_is_not() {
    let updates = read_pending_updates(100, OBSOLETING).unwrap();

    let names: Vec<&str> = updates
        .iter()
        .map(|update| update.package_name.as_str())
        .collect();
    assert_eq!(
        names,
        [
            "maran-fixture-c++.lib",
            "maran-fixture-package-with-an-exceptionally-long-name-to-force-wrapping",
            "maran-fixture-replacement"
        ]
    );
}

#[test]
fn status_zero_with_no_output_is_an_up_to_date_host() {
    assert_eq!(read_pending_updates(0, UP_TO_DATE), Ok(Vec::new()));
}

#[test]
fn status_zero_with_output_is_not_read_as_an_up_to_date_host() {
    assert_eq!(
        read_pending_updates(0, WITH_UPDATES),
        Err(PendingUpdatesFailure::UnrecognisedLine { line: 2 })
    );
}

/// The case the plan's risk list names: a cache that was never built exits 1
/// with a message on stderr and nothing on stdout, and that must never read as
/// "no updates".
#[test]
fn a_missing_cache_exits_one_and_is_a_failure_and_not_an_empty_list() {
    assert!(NO_CACHE_MESSAGE.contains("Cache-only enabled but no cache"));

    assert_eq!(
        read_pending_updates(1, ""),
        Err(PendingUpdatesFailure::Unsuccessful { status: 1 })
    );
}

#[test]
fn no_status_but_zero_and_one_hundred_is_a_success() {
    for status in [-1, 1, 2, 3, 99, 101, 255] {
        assert_eq!(
            read_pending_updates(status, WITH_UPDATES),
            Err(PendingUpdatesFailure::Unsuccessful { status }),
            "status {status}"
        );
    }
}

#[test]
fn status_one_hundred_with_no_row_is_incomplete_and_not_an_empty_list() {
    assert_eq!(
        read_pending_updates(100, ""),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn a_row_that_is_not_three_fields_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}wrapped-name.x86_64\n");
    let line = output.lines().count();

    assert_eq!(
        read_pending_updates(100, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_with_a_fourth_field_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}extra.x86_64 1.0-1 baseos surprise\n");

    assert!(matches!(
        read_pending_updates(100, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}

#[test]
fn a_name_without_an_architecture_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}noarchitecture 1.0-1 baseos\n");

    assert!(matches!(
        read_pending_updates(100, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}

#[test]
fn an_indented_line_outside_the_obsoleting_section_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}    stray.x86_64 1.0-1 @system\n");

    assert!(matches!(
        read_pending_updates(100, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}

#[test]
fn a_heading_other_than_the_obsoleting_one_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}\nSecurity Updates\n");

    assert!(matches!(
        read_pending_updates(100, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}
