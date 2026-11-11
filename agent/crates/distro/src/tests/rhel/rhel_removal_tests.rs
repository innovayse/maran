//! Tests for reading `dnf remove --assumeno` (issue #527).
//!
//! The `RECORDED_*` outputs are real runs of `dnf remove --assumeno` on `almalinux:9` (dnf
//! 4.14.0); the `WITH_DEPENDENT_AND_UNUSED` output is synthetic (a wrapped row, which the
//! recorded runs do not contain) and written from the same table grammar.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{read_removal_simulation, removal_simulation_arguments};
use crate::RemovalSimulationFailure;

/// `dnf remove --assumeno httpd` with `httpd` and `mod_ssl` installed (exit 1).
const RECORDED_HTTPD: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_remove_simulation_httpd.txt");

/// `dnf remove --assumeno mod_ssl` (exit 1).
const RECORDED_MOD_SSL: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_remove_simulation_mod_ssl.txt");

/// `dnf remove --assumeno nosuchpkg` (exit 0, nothing to do).
const RECORDED_NOT_INSTALLED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_remove_not_installed.txt");

const WITH_DEPENDENT_AND_UNUSED: &str = "\
Dependencies resolved.
================================================================================
 Package            Architecture  Version            Repository           Size
================================================================================
Removing:
 httpd              x86_64        2.4.57-8.el9       @appstream           5.9 M
Removing dependent packages:
 mod_ssl            x86_64        1:2.4.57-8.el9     @appstream           230 k
 a-package-with-a-very-long-name-that-wraps
                    noarch        1.0-1.el9          @appstream            1 k
Removing unused dependencies:
 apr                x86_64        1.7.0-12.el9_3     @appstream           316 k

Transaction Summary
================================================================================
Remove  4 Packages

Freed space: 6.5 M
Operation aborted.
";

#[test]
fn the_sections_are_told_apart_and_a_wrapped_row_is_read_once() {
    let plan = read_removal_simulation(1, WITH_DEPENDENT_AND_UNUSED).unwrap();

    assert_eq!(
        plan.removed,
        vec![
            "httpd",
            "mod_ssl",
            "a-package-with-a-very-long-name-that-wraps"
        ]
    );
    assert_eq!(plan.unused, vec!["apr"]);
}

#[test]
fn a_plan_with_the_requested_package_alone_has_no_dependents() {
    let output = "Removing:\n httpd  x86_64  2.4  @appstream  5.9 M\n\nTransaction Summary\nRemove  1 Package\n";

    let plan = read_removal_simulation(0, output).unwrap();

    assert_eq!(plan.removed, vec!["httpd"]);
    assert!(plan.unused.is_empty());
}

#[test]
fn a_status_outside_the_dry_runs_a_missing_summary_and_an_unknown_section_are_failures() {
    assert_eq!(
        read_removal_simulation(2, WITH_DEPENDENT_AND_UNUSED),
        Err(RemovalSimulationFailure::Unsuccessful { status: 2 })
    );
    assert_eq!(
        read_removal_simulation(1, "Error: No match for argument: httpd\n"),
        Err(RemovalSimulationFailure::Unrecognised)
    );
    assert_eq!(
        read_removal_simulation(1, "Removing:\n httpd  x86_64  2.4  @appstream  5.9 M\n"),
        Err(RemovalSimulationFailure::Unrecognised)
    );
    let installing =
        WITH_DEPENDENT_AND_UNUSED.replace("Removing unused dependencies:", "Installing:");
    assert_eq!(
        read_removal_simulation(1, &installing),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn the_dry_run_answers_no_and_names_the_package_after_the_end_of_options() {
    let arguments = removal_simulation_arguments("httpd");

    assert_eq!(arguments, ["remove", "--assumeno", "--", "httpd"]);
}

#[test]
fn the_recorded_runs_are_read_with_their_unused_dependencies_apart() {
    let httpd = read_removal_simulation(1, RECORDED_HTTPD).unwrap();
    assert_eq!(httpd.removed, vec!["httpd"]);
    assert_eq!(
        httpd.unused,
        vec!["almalinux-logos-httpd", "libbrotli", "mod_http2", "mod_lua"]
    );

    let mod_ssl = read_removal_simulation(1, RECORDED_MOD_SSL).unwrap();
    assert_eq!(mod_ssl.removed, vec!["mod_ssl"]);
    assert_eq!(mod_ssl.unused, vec!["libtalloc", "sscg"]);
}

#[test]
fn the_recorded_run_of_a_package_that_is_not_installed_is_no_plan() {
    assert_eq!(
        read_removal_simulation(0, RECORDED_NOT_INSTALLED),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn a_line_the_reader_does_not_know_refuses_the_read_wherever_it_is() {
    // Inverse controls of the two recorded plans: the same output with one extra line.
    for extra in [
        "Warning: something new\n",
        "Installing dependencies:\n",
        "Downgrading:\n",
    ] {
        let before_table = format!("{extra}{RECORDED_HTTPD}");
        let after_summary = format!("{RECORDED_HTTPD}{extra}");
        assert_eq!(
            read_removal_simulation(1, &before_table),
            Err(RemovalSimulationFailure::Unrecognised),
            "before: {extra}"
        );
        assert_eq!(
            read_removal_simulation(1, &after_summary),
            Err(RemovalSimulationFailure::Unrecognised),
            "after: {extra}"
        );
    }
}

#[test]
fn a_row_the_reader_cannot_parse_refuses_the_read_and_never_drops_a_package() {
    // A row with an extra indent (a layout it does not know), a row with too few columns, and a
    // wrapped name whose columns never come.
    for broken in [
        RECORDED_HTTPD.replace(" mod_http2 ", "  mod_http2 "),
        RECORDED_HTTPD.replace(
            " mod_lua                 x86_64   2.4.62-13.el9_8.9          @appstream   142 k",
            " mod_lua                 x86_64",
        ),
        RECORDED_HTTPD.replace(
            " mod_lua                 x86_64   2.4.62-13.el9_8.9          @appstream   142 k",
            " mod_lua",
        ),
    ] {
        assert_eq!(
            read_removal_simulation(1, &broken),
            Err(RemovalSimulationFailure::Unrecognised),
            "{broken}"
        );
    }
}

#[test]
fn the_metadata_age_line_a_cold_cache_prints_is_not_a_surprise() {
    // Accepting control: known noise in front of the plan does not refuse it.
    let output = format!(
        "Last metadata expiration check: 0:00:03 ago on Tue Nov 10 10:00:00 2026.\n{RECORDED_HTTPD}"
    );

    assert_eq!(
        read_removal_simulation(1, &output).unwrap().removed,
        vec!["httpd"]
    );
}
