//! Tests for reading `apt-get -s remove` (issue #527).
//!
//! `apt_remove_simulation.txt` is a recorded run on `ubuntu:24.04`; the dependents case is that
//! run's grammar with a second `Remv` line.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{read_removal_simulation, removal_simulation_arguments};
use crate::RemovalSimulationFailure;

/// `apt-get -s remove curl`.
const RECORDED: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_remove_simulation.txt");

#[test]
fn the_recorded_run_removes_the_requested_package_only() {
    let plan = read_removal_simulation(0, RECORDED).unwrap();

    assert_eq!(plan.removed, vec!["curl"]);
    assert!(plan.unused.is_empty());
}

#[test]
fn a_dependent_and_an_architecture_qualifier_are_read() {
    let output = "The following packages will be REMOVED:\n  apache2 python3-certbot-apache\n\
                  Remv python3-certbot-apache [2.9.0-1]\nRemv apache2:amd64 [2.4.58-1]\n";

    let plan = read_removal_simulation(0, output).unwrap();

    assert_eq!(plan.removed, vec!["python3-certbot-apache", "apache2"]);
}

#[test]
fn a_failed_run_and_a_run_with_no_plan_are_failures_and_never_an_empty_plan() {
    assert_eq!(
        read_removal_simulation(100, RECORDED),
        Err(RemovalSimulationFailure::Unsuccessful { status: 100 })
    );
    assert_eq!(
        read_removal_simulation(0, "Reading package lists...\n"),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn the_dry_run_names_the_package_after_the_end_of_options() {
    let arguments = removal_simulation_arguments("-evil");

    assert_eq!(arguments[0], "-s");
    assert_eq!(&arguments[arguments.len() - 2..], ["--", "-evil"]);
}

#[test]
fn a_line_the_reader_does_not_know_refuses_the_read() {
    // Inverse controls of the recorded run: an install, a purge, a configure, another list and a
    // count line that upgrades or installs something.
    for broken in [
        format!("{RECORDED}Inst libfoo (1.0 Ubuntu:24.04 [amd64])\n"),
        format!("{RECORDED}Purg curl [8.5.0-2ubuntu10.16]\n"),
        format!("{RECORDED}Conf libfoo (1.0 Ubuntu:24.04 [amd64])\n"),
        RECORDED.replace(
            "The following packages will be REMOVED:",
            "The following packages will be upgraded:",
        ),
        RECORDED.replace(
            "0 upgraded, 0 newly installed",
            "0 upgraded, 1 newly installed",
        ),
        RECORDED.replace(
            "0 upgraded, 0 newly installed",
            "2 upgraded, 0 newly installed",
        ),
        format!("Surprise\n{RECORDED}"),
    ] {
        assert_eq!(
            read_removal_simulation(0, &broken),
            Err(RemovalSimulationFailure::Unrecognised),
            "{broken}"
        );
    }
}
