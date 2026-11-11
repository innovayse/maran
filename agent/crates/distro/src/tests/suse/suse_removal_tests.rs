//! Tests for reading `zypper remove --dry-run` (issue #527).
//!
//! The `RECORDED_*` outputs are real runs of `zypper --non-interactive remove --dry-run` on
//! `opensuse/leap:latest` (Leap 16.0, zypper 1.14.101); the other outputs are synthetic and use
//! the same plan sections.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{read_removal_simulation, removal_simulation_arguments};
use crate::RemovalSimulationFailure;

/// `zypper --non-interactive remove --dry-run -- apache2` with `apache2` installed (exit 0).
const RECORDED_APACHE2: &str =
    include_str!("../../../tests/fixtures/package_manager/zypper_remove_dry_run_apache2.txt");

/// The same for `apache2-utils`, which nothing depends on (exit 0).
const RECORDED_UTILS: &str =
    include_str!("../../../tests/fixtures/package_manager/zypper_remove_dry_run_utils.txt");

/// The same for a package no repository or installed package has (exit 104).
const RECORDED_NOT_FOUND: &str =
    include_str!("../../../tests/fixtures/package_manager/zypper_remove_not_found.txt");

#[test]
fn a_single_package_and_a_list_of_packages_are_read() {
    let single = "Resolving package dependencies...\n\nThe following package is going to be REMOVED:\n  apache2\n\n1 package to remove.\n";
    let several = "The following 2 packages are going to be REMOVED:\n  apache2 apache2-utils\n  python3-certbot-apache\n\n3 packages to remove.\n";

    assert_eq!(
        read_removal_simulation(0, single).unwrap().removed,
        vec!["apache2"]
    );
    assert_eq!(
        read_removal_simulation(0, several).unwrap().removed,
        vec!["apache2", "apache2-utils", "python3-certbot-apache"]
    );
    assert!(
        read_removal_simulation(0, single)
            .unwrap()
            .unused
            .is_empty()
    );
}

#[test]
fn failures_are_never_an_empty_plan() {
    let installing = "The following package is going to be INSTALLED:\n  foo\n";

    assert_eq!(
        read_removal_simulation(104, "The following package is going to be REMOVED:\n  a\n"),
        Err(RemovalSimulationFailure::Unsuccessful { status: 104 })
    );
    assert_eq!(
        read_removal_simulation(0, "Nothing to do.\n"),
        Err(RemovalSimulationFailure::Unrecognised)
    );
    assert_eq!(
        read_removal_simulation(0, installing),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn the_dry_run_is_non_interactive_and_names_the_package_after_the_end_of_options() {
    assert_eq!(
        removal_simulation_arguments("apache2"),
        ["--non-interactive", "remove", "--dry-run", "--", "apache2"]
    );
}

#[test]
fn the_recorded_runs_name_the_mpm_that_goes_with_apache2() {
    assert_eq!(
        read_removal_simulation(0, RECORDED_APACHE2)
            .unwrap()
            .removed,
        vec!["apache2", "apache2-prefork"]
    );
    assert_eq!(
        read_removal_simulation(0, RECORDED_UTILS).unwrap().removed,
        vec!["apache2-utils"]
    );
}

#[test]
fn the_recorded_run_of_an_unknown_package_is_a_failure_whatever_its_status() {
    assert_eq!(
        read_removal_simulation(104, RECORDED_NOT_FOUND),
        Err(RemovalSimulationFailure::Unsuccessful { status: 104 })
    );
    // Inverse control: even read as a success it holds no removal section.
    assert_eq!(
        read_removal_simulation(0, RECORDED_NOT_FOUND),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn a_line_or_a_word_the_reader_does_not_know_refuses_the_read() {
    // Inverse controls of the recorded plan: one extra line, one extra section, one odd word.
    for broken in [
        format!("Something new happened\n{RECORDED_APACHE2}"),
        format!("{RECORDED_APACHE2}Another line\n"),
        format!(
            "The following package is not supported by its vendor:\n  foo\n\n{RECORDED_APACHE2}"
        ),
        RECORDED_APACHE2.replace("apache2-prefork", "apache2-prefork(1.0)"),
    ] {
        assert_eq!(
            read_removal_simulation(0, &broken),
            Err(RemovalSimulationFailure::Unrecognised),
            "{broken}"
        );
    }
}
