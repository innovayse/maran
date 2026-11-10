//! Tests for how a failed `apt-get install` is classified (issue #251).
//!
//! Every output below is what `apt-get` printed on `ubuntu:24.04`, recorded on
//! 2026-10-31 into `tests/fixtures/package_manager/`: a run with no network at
//! all, a run whose mirror answered every archive with `404`, and a run asked
//! for a package no configured repository carries. `apt-get` exited 100 in all
//! three.
//!
//! The pending-updates tests read what `apt-get -s -q -o Debug::NoLocking=true
//! dist-upgrade` printed on `ubuntu:24.04` containers, recorded on 2026-11-03:
//! a `noble-20240429` image after `apt-get update` (updates available, one with
//! a package held back), the same image after upgrading (none), and a fresh
//! image with no lists at all. `apt_remove_simulation.txt` is a recorded
//! `apt-get -s remove`: it is the only real source of a `Remv` line, which the
//! upgrade simulations did not print. All exit 0.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{
    CATALOGUE_PROBE_ARGUMENTS, PENDING_UPDATES_ARGUMENTS, classify_origins,
    classify_package_failure, package_manager, read_catalogue_probe, read_pending_updates,
    read_reboot_probe_run,
};
use crate::{PackageFailure, PendingUpdatesFailure, RebootProbeFailure, UpdateClassification};

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
fn a_package_no_repository_carries_is_not_available() {
    // The mirror was reached and answered; the HOST's repository set is what
    // lacks the package, so blaming the mirror would send the operator away
    // from the configuration that has to change.
    assert_eq!(
        classify_package_failure(100, PACKAGE_MISSING),
        PackageFailure::NotAvailable
    );
}

#[test]
fn a_missing_package_beside_a_fetch_failure_stays_the_sources_fault() {
    // Inverse control: a source that failed may be the reason the package was
    // not found, so the more specific source verdict wins.
    let both = format!("{SOURCE_UNREACHABLE}{PACKAGE_MISSING}");

    assert_eq!(
        classify_package_failure(100, &both),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_missing_package_under_a_status_apt_does_not_use_is_local() {
    assert_eq!(
        classify_package_failure(1, PACKAGE_MISSING),
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

/// `dist-upgrade` simulation after `apt-get update` on an image from April 2024:
/// 67 packages upgraded, one new dependency, several origins per package.
const WITH_UPDATES: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_dist_upgrade_with_updates.txt");

/// The same simulation after the upgrade was really applied.
const UP_TO_DATE: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_dist_upgrade_up_to_date.txt");

/// The same simulation on an image whose lists directory is empty: apt says
/// nothing is to be upgraded and exits 0.
const EMPTY_LISTS: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_dist_upgrade_empty_lists.txt");

/// The simulation with `tar` held: apt keeps it back and prints no `Inst` line.
const WITH_HELD_PACKAGE: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_dist_upgrade_with_held_package.txt");

/// `apt-get -s remove curl`, which prints a `Remv` line.
const REMOVE_SIMULATION: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_remove_simulation.txt");

/// `apt-get indextargets` after `apt-get update`: one line per index file.
const INDEX_TARGETS_BUILT: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_indextargets_built.txt");

/// `apt-get indextargets` against an empty lists directory: no output at all.
const INDEX_TARGETS_EMPTY: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_indextargets_empty.txt");

/// The update named `name` in a read, or a panic naming the package.
fn row<'a>(updates: &'a [crate::PendingUpdate], name: &str) -> &'a crate::PendingUpdate {
    updates
        .iter()
        .find(|update| update.package_name == name)
        .unwrap_or_else(|| panic!("no row for {name}"))
}

#[test]
fn the_pending_updates_run_is_a_simulation_with_no_operand() {
    assert_eq!(
        PENDING_UPDATES_ARGUMENTS,
        &["-s", "-q", "-o", "Debug::NoLocking=true", "dist-upgrade"]
    );
}

#[test]
fn the_catalogue_probe_only_lists_index_files() {
    assert_eq!(
        CATALOGUE_PROBE_ARGUMENTS,
        &["indextargets", "--format", "$(FILENAME)"]
    );
}

#[test]
fn every_package_apt_would_upgrade_is_a_row() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(updates.len(), 67);
}

#[test]
fn a_row_carries_the_installed_version_the_new_version_and_every_origin() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let update = row(&updates, "gcc-14-base");
    assert_eq!(update.available_version, "14.2.0-4ubuntu2~24.04.1");
    assert_eq!(
        update.installed_version.as_deref(),
        Some("14-20240412-0ubuntu1")
    );
    assert_eq!(
        update.source,
        "Ubuntu:24.04/noble-updates, Ubuntu:24.04/noble-security"
    );
}

#[test]
fn a_package_with_one_origin_reports_that_origin_without_its_architecture() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let update = row(&updates, "base-files");
    assert_eq!(update.available_version, "13ubuntu10.5");
    assert_eq!(update.source, "Ubuntu:24.04/noble-updates");
}

#[test]
fn an_epoch_stays_part_of_the_version() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let update = row(&updates, "bsdutils");
    assert_eq!(
        update.installed_version.as_deref(),
        Some("1:2.39.3-9ubuntu6")
    );
    assert_eq!(update.available_version, "1:2.39.3-9ubuntu6.6");
}

#[test]
fn a_package_name_with_a_plus_and_a_dot_is_read_whole() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "libstdc++6").available_version,
        "14.2.0-4ubuntu2~24.04.1"
    );
    assert_eq!(row(&updates, "libapt-pkg6.0t64").available_version, "2.8.3");
}

#[test]
fn a_dependency_apt_would_newly_install_is_not_an_update() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert!(
        updates
            .iter()
            .all(|update| update.package_name != "libdb5.3t64"),
        "a new dependency has no installed version and is not an update"
    );
}

#[test]
fn conf_lines_add_no_row_of_their_own() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let names: std::collections::BTreeSet<&str> = updates
        .iter()
        .map(|update| update.package_name.as_str())
        .collect();
    assert_eq!(names.len(), updates.len(), "one row per package");
}

#[test]
fn a_package_apt_keeps_back_is_not_reported() {
    let updates = read_pending_updates(0, WITH_HELD_PACKAGE).unwrap();

    assert_eq!(updates.len(), 66);
    assert!(updates.iter().all(|update| update.package_name != "tar"));
}

#[test]
fn a_removal_is_read_and_adds_no_row() {
    let updates = read_pending_updates(0, REMOVE_SIMULATION).unwrap();

    assert!(updates.is_empty());
}

#[test]
fn an_up_to_date_host_is_an_empty_list() {
    let updates = read_pending_updates(0, UP_TO_DATE).unwrap();

    assert!(updates.is_empty());
}

/// The simulation alone cannot tell a catalogue that was never built from an
/// up-to-date host: this is the recorded fact that makes the probe necessary,
/// stated as a test so the probe cannot be removed unnoticed.
#[test]
fn an_empty_lists_directory_reads_as_up_to_date_by_the_simulation_alone() {
    let updates = read_pending_updates(0, EMPTY_LISTS).unwrap();

    assert!(updates.is_empty());
}

#[test]
fn a_built_catalogue_passes_the_probe() {
    assert_eq!(read_catalogue_probe(0, INDEX_TARGETS_BUILT), Ok(()));
}

#[test]
fn an_empty_lists_directory_fails_the_probe() {
    assert_eq!(
        read_catalogue_probe(0, INDEX_TARGETS_EMPTY),
        Err(PendingUpdatesFailure::CatalogueNotBuilt)
    );
}

#[test]
fn a_probe_that_exits_non_zero_is_unsuccessful_even_with_output() {
    assert_eq!(
        read_catalogue_probe(100, INDEX_TARGETS_BUILT),
        Err(PendingUpdatesFailure::Unsuccessful { status: 100 })
    );
}

#[test]
fn a_simulation_that_exits_non_zero_is_unsuccessful_even_with_output() {
    assert_eq!(
        read_pending_updates(100, WITH_UPDATES),
        Err(PendingUpdatesFailure::Unsuccessful { status: 100 })
    );
}

#[test]
fn a_line_apt_is_not_known_to_print_fails_the_whole_read() {
    let output = format!("{WITH_UPDATES}something apt never printed\n");
    let line = output.lines().count();

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn an_inst_line_that_does_not_parse_fails_the_whole_read() {
    let output = WITH_UPDATES.replacen(
        "Inst base-files [13ubuntu10]",
        "Inst base-files 13ubuntu10",
        1,
    );

    assert!(matches!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}

#[test]
fn an_inst_line_with_text_after_its_group_fails_the_whole_read() {
    let output = WITH_UPDATES.replacen(
        "Ubuntu:24.04/noble-updates [amd64])",
        "Ubuntu:24.04/noble-updates [amd64]) trailing",
        1,
    );

    assert!(matches!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { .. })
    ));
}

#[test]
fn output_cut_before_the_summary_is_incomplete_and_not_a_shorter_list() {
    let cut = WITH_UPDATES
        .lines()
        .filter(|line| !line.contains(" upgraded, "))
        .collect::<Vec<_>>()
        .join("\n");

    assert_eq!(
        read_pending_updates(0, &cut),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn empty_output_is_incomplete_and_not_an_up_to_date_host() {
    assert_eq!(
        read_pending_updates(0, ""),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn an_update_published_by_the_security_suite_is_security() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "gcc-14-base").classification,
        UpdateClassification::Security
    );
    assert_eq!(
        row(&updates, "libc6").classification,
        UpdateClassification::Security
    );
}

#[test]
fn an_update_published_only_by_the_updates_suite_is_ordinary() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "base-files").classification,
        UpdateClassification::Ordinary
    );
}

#[test]
fn a_recorded_read_has_both_classifications_and_none_unmeasured() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert!(
        updates
            .iter()
            .any(|update| update.classification == UpdateClassification::Security)
    );
    assert!(
        updates
            .iter()
            .any(|update| update.classification == UpdateClassification::Ordinary)
    );
    assert!(
        updates
            .iter()
            .all(|update| update.classification != UpdateClassification::Unmeasured)
    );
}

#[test]
fn a_current_debian_security_suite_is_security() {
    assert_eq!(
        classify_origins("Debian-Security:12/stable-security"),
        UpdateClassification::Security
    );
}

#[test]
fn one_security_origin_among_several_makes_the_update_security() {
    assert_eq!(
        classify_origins("Debian:12.5/stable, Debian-Security:12/stable-security"),
        UpdateClassification::Security
    );
}

#[test]
fn a_suite_that_only_contains_security_in_its_name_is_ordinary() {
    assert_eq!(
        classify_origins("Ubuntu:24.04/noble-security-extras"),
        UpdateClassification::Ordinary
    );
}

#[test]
fn a_security_spelling_in_the_origin_name_and_not_the_suite_is_ordinary() {
    assert_eq!(
        classify_origins("Vendor-security:1/stable"),
        UpdateClassification::Ordinary
    );
}

#[test]
fn an_update_with_no_origin_is_ordinary() {
    assert_eq!(classify_origins(""), UpdateClassification::Ordinary);
}

#[test]
fn this_family_has_no_program_run_to_read_a_reboot_status_from() {
    for status in [0, 1, 127] {
        assert_eq!(
            read_reboot_probe_run(status),
            Err(RebootProbeFailure::NotAProgramProbe)
        );
    }
}
