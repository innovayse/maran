//! Tests for reading `dnf remove --assumeno` (issue #527).
//!
//! The `RECORDED_*` outputs are real runs of `dnf remove --assumeno` on `almalinux:9` (dnf
//! 4.14.0); the `WITH_DEPENDENT_AND_UNUSED` output is synthetic (a wrapped row, which the
//! recorded runs do not contain) and written from the same table grammar.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_agent_core::validation::web::node_major::NodeMajor;

use super::{
    nodejs_removal_simulation_arguments, read_removal_simulation, removal_simulation_arguments,
};
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

/// `dnf module remove --assumeno nodejs:20/common` with that profile installed (exit 1).
const RECORDED_NODEJS_20: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_remove_simulation_nodejs20.txt"
);

/// The same with the 22 stream installed, whose plan also lists `nodejs-libs` (exit 1).
const RECORDED_NODEJS_22: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_remove_simulation_nodejs22.txt"
);

/// `dnf module remove --assumeno nodejs:22/common` with nothing installed (exit 0, nothing to do).
const RECORDED_MODULE_NOT_INSTALLED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_module_remove_not_installed.txt");

/// `dnf module remove --assumeno nodejs:20/common` on a host whose Node.js 16 came from the plain
/// package (exit 0, `Unable to match profile`, nothing to do).
const RECORDED_UNMANAGED_NODE: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_remove_nodejs_unmanaged_plain_install.txt"
);

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

#[test]
fn the_dry_run_of_a_stream_removal_follows_the_module_spec_after_the_double_dash() {
    let stream = NodejsModuleStream::from_major(NodeMajor::parse("20").unwrap());

    assert_eq!(
        nodejs_removal_simulation_arguments(&stream),
        vec!["module", "remove", "--assumeno", "--", "nodejs:20/common"]
    );
}

#[test]
fn a_stream_removal_reads_as_the_packages_it_takes_and_the_module_state_is_known() {
    let plan = read_removal_simulation(1, RECORDED_NODEJS_20).unwrap();

    assert_eq!(plan.removed, vec!["nodejs", "npm"]);
    assert_eq!(
        plan.unused,
        vec!["libbrotli", "nodejs-docs", "nodejs-full-i18n"]
    );
}

#[test]
fn a_stream_removal_that_lists_the_library_package_reads_it_among_the_unused() {
    let plan = read_removal_simulation(1, RECORDED_NODEJS_22).unwrap();

    assert_eq!(plan.removed, vec!["nodejs", "npm"]);
    assert!(plan.unused.contains(&"nodejs-libs".to_owned()));
}

#[test]
fn a_stream_removal_with_a_dependent_package_reads_the_dependent_as_removed() {
    // Inverse control of the recorded plan: a package that needs nodejs is taken with it, and
    // the caller's containment check must see it among `removed`.
    let output = RECORDED_NODEJS_20.replace(
        "Removing unused dependencies:",
        "Removing dependent packages:\n some-addon           noarch  1.0-1.el9                                       @appstream   10 k\nRemoving unused dependencies:",
    );

    let plan = read_removal_simulation(1, &output).unwrap();

    assert!(plan.removed.contains(&"some-addon".to_owned()));
}

#[test]
fn every_module_section_header_is_known_and_collects_no_package() {
    for header in [
        "Disabling module profiles:",
        "Disabling modules:",
        "Resetting modules:",
    ] {
        let output = RECORDED_NODEJS_20.replace(
            "Disabling module profiles:\n nodejs/common",
            &format!("{header}\n nodejs/common"),
        );

        let plan = read_removal_simulation(1, &output).unwrap();

        assert_eq!(plan.removed, vec!["nodejs", "npm"], "{header}");
    }
}

#[test]
fn a_module_header_this_reader_does_not_know_still_refuses_the_read() {
    // Inverse control of the three known module headers.
    let output =
        RECORDED_NODEJS_20.replace("Disabling module profiles:", "Switching module streams:");

    assert_eq!(
        read_removal_simulation(1, &output),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn a_module_row_the_reader_cannot_parse_refuses_the_read() {
    for broken in [
        // An extra indent, a shell metacharacter in a spec and four words in a row.
        RECORDED_NODEJS_20.replace(" nodejs/common", "  nodejs/common"),
        RECORDED_NODEJS_20.replace(" nodejs/common", " nodejs/common;reboot"),
        RECORDED_NODEJS_20.replace(" nodejs/common", " nodejs 20 common extra"),
    ] {
        assert_eq!(
            read_removal_simulation(1, &broken),
            Err(RemovalSimulationFailure::Unrecognised),
            "{broken}"
        );
    }
}

#[test]
fn a_stream_that_is_not_installed_is_not_a_plan() {
    assert_eq!(
        read_removal_simulation(0, RECORDED_MODULE_NOT_INSTALLED),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

#[test]
fn the_removal_of_a_stream_over_a_plain_node_install_is_not_a_plan() {
    // Why the panel must tell this host apart before it asks: the answer is never a plan.
    assert_eq!(
        read_removal_simulation(0, RECORDED_UNMANAGED_NODE),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}

/// `dnf remove --assumeno nodejs` with `nodejs`, `nodejs-npm`, `nodejs24` and `nodejs24-npm`
/// installed on `almalinux:10` (10.2, dnf 4.20.0; exit 1): the default major and what needs it.
const EL10_NODEJS: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_remove_simulation_nodejs_el10.txt");

/// The same for `nodejs-npm` alone.
const EL10_NODEJS_NPM: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_remove_simulation_nodejs_npm_el10.txt"
);

/// The same for `nodejs24`.
const EL10_NODEJS_24: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_remove_simulation_nodejs24_el10.txt");

/// The same for `nodejs24-npm` alone.
const EL10_NODEJS_24_NPM: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_remove_simulation_nodejs24_npm_el10.txt"
);

/// The same for `nodejs24` with nothing else of Node.js installed: its libraries are unused too.
const EL10_NODEJS_24_ALONE: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_remove_simulation_nodejs24_alone_el10.txt"
);

#[test]
fn the_recorded_removal_of_the_default_node_major_takes_its_npm_and_nothing_of_24() {
    let plan = read_removal_simulation(1, EL10_NODEJS).unwrap();

    assert_eq!(plan.removed, vec!["nodejs", "nodejs-npm"]);
    assert!(plan.unused.iter().all(|name| !name.starts_with("nodejs24")));
    assert!(plan.unused.contains(&"nodejs-libs".to_owned()));
}

#[test]
fn the_recorded_removal_of_a_numbered_node_major_takes_its_own_npm_and_nothing_else() {
    let plan = read_removal_simulation(1, EL10_NODEJS_24).unwrap();

    assert_eq!(plan.removed, vec!["nodejs24", "nodejs24-npm"]);
    assert_eq!(
        plan.unused,
        vec!["nodejs24-docs", "nodejs24-full-i18n", "nodejs24-libs"]
    );
}

#[test]
fn removing_only_an_npm_package_removes_only_it() {
    assert_eq!(
        read_removal_simulation(1, EL10_NODEJS_NPM).unwrap().removed,
        vec!["nodejs-npm"]
    );
    assert_eq!(
        read_removal_simulation(1, EL10_NODEJS_24_NPM)
            .unwrap()
            .removed,
        vec!["nodejs24-npm"]
    );
}

#[test]
fn the_shared_libraries_a_last_node_major_leaves_unused_are_read_as_unused_not_removed() {
    // `libuv`, `c-ares` and `libbrotli` are unused dependencies when no other major needs them.
    // They are what the install pulled in; the reader keeps them out of the removed list, which
    // is the list the app's own set is held to.
    let plan = read_removal_simulation(1, EL10_NODEJS_24_ALONE).unwrap();

    assert_eq!(plan.removed, vec!["nodejs24", "nodejs24-npm"]);
    for shared in ["libuv", "c-ares", "libbrotli"] {
        assert!(plan.unused.contains(&shared.to_owned()), "{shared}");
        assert!(!plan.removed.contains(&shared.to_owned()), "{shared}");
    }
}

#[test]
fn a_plan_of_the_recorded_shape_with_an_unknown_section_is_refused() {
    // Inverse control of the readings above: the same text with a heading dnf does not print.
    let broken = EL10_NODEJS_24.replace("Removing dependent packages:", "Downgrading:");

    assert_eq!(
        read_removal_simulation(1, &broken),
        Err(RemovalSimulationFailure::Unrecognised)
    );
}
