//! Tests for the RHEL family's Node.js module streams (issue #532).
//!
//! The `RECORDED_*` outputs are real runs of `dnf -C module list nodejs` on `almalinux:9` (dnf
//! 4.14.0); the failure statuses are those the same command exited with on a host with no
//! metadata cache (`Error: Cache-only enabled but no cache for 'appstream'`, exit 1) and for a
//! module that does not exist (`Error: No matching Modules to list`, exit 1).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_agent_core::validation::web::node_major::NodeMajor;

use super::{
    nodejs_module_packages, nodejs_stream_list_arguments, nodejs_stream_release_command,
    nodejs_unmanaged_removal_command, read_nodejs_streams,
};
use crate::PackageQueryFailure;

/// `dnf -C module list nodejs` after a metadata refresh, nothing installed (exit 0).
const RECORDED_CACHED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_module_list_nodejs_cached.txt");

/// `dnf module list nodejs` that refreshed its repositories first (exit 0): the download
/// progress lines come before the listing.
const RECORDED_REFRESHING: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_module_list_nodejs_refreshing.txt");

/// `dnf -C module list nodejs` with `nodejs:20/common` installed (exit 0): the enabled stream
/// carries `[e]` and its profile `[i]`.
const RECORDED_INSTALLED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_module_list_nodejs_installed.txt");

/// The same listing on a host where `dnf install nodejs` took the plain package (Node.js 16): no
/// stream carries a marker.
const RECORDED_UNMANAGED: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_list_nodejs_unmanaged_plain_install.txt"
);

/// Standard error of `dnf -C module list nodejs` on `almalinux:10` with a warm cache (exit 1).
const RECORDED_NO_MODULES: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_list_nodejs_no_modules.stderr.txt"
);

/// Standard error of the same command on `almalinux:9` with no metadata cache (exit 1).
const RECORDED_NO_CACHE: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_list_nodejs_no_cache.stderr.txt"
);

/// Standard error of the same command on `almalinux:10` with no metadata cache (exit 1).
const RECORDED_NO_CACHE_EL10: &str = include_str!(
    "../../../tests/fixtures/package_manager/dnf_module_list_nodejs_no_cache_el10.stderr.txt"
);

fn stream(digits: &str) -> NodejsModuleStream {
    NodejsModuleStream::from_major(NodeMajor::parse(digits).unwrap())
}

fn four_streams() -> Vec<NodejsModuleStream> {
    vec![stream("18"), stream("20"), stream("22"), stream("24")]
}

#[test]
fn the_listing_is_cache_only_and_names_only_the_nodejs_module() {
    assert_eq!(
        nodejs_stream_list_arguments(),
        vec!["-C", "module", "list", "nodejs"]
    );
}

#[test]
fn the_four_recorded_streams_are_read_in_the_order_they_are_listed() {
    assert_eq!(
        read_nodejs_streams(0, RECORDED_CACHED, "").unwrap().offered,
        four_streams()
    );
}

#[test]
fn the_download_progress_in_front_of_the_listing_is_not_a_row() {
    assert_eq!(
        read_nodejs_streams(0, RECORDED_REFRESHING, "")
            .unwrap()
            .offered,
        four_streams()
    );
}

#[test]
fn an_enabled_stream_and_an_installed_profile_still_read_as_the_same_stream() {
    // The markers move the columns: `20 [e]` and `common [d] [i],`.
    assert_eq!(
        read_nodejs_streams(0, RECORDED_INSTALLED, "")
            .unwrap()
            .offered,
        four_streams()
    );
}

#[test]
fn a_stream_outside_the_catalogue_is_read_and_left_out() {
    let listing = "nodejs 16     common [d], development\nnodejs 20     common [d], development\nnodejs latest common\n";

    assert_eq!(
        read_nodejs_streams(0, listing, "").unwrap().offered,
        vec![stream("20")]
    );
}

#[test]
fn a_stream_without_the_common_profile_is_left_out() {
    // Inverse control of the recorded listings: the same row, with the profile renamed.
    let listing = "nodejs 20     development, minimal Javascript runtime\nnodejs 22     minimal, common Javascript runtime\n";

    assert_eq!(
        read_nodejs_streams(0, listing, "").unwrap().offered,
        vec![stream("22")]
    );
}

#[test]
fn a_stream_listed_twice_is_offered_once() {
    let listing = "nodejs 20     common [d], development\nnodejs 20     common [d], development\n";

    assert_eq!(
        read_nodejs_streams(0, listing, "").unwrap().offered,
        vec![stream("20")]
    );
}

#[test]
fn a_row_of_another_module_is_not_a_nodejs_row() {
    let listing = "nodejs-devel 20 common\nruby 3.3 common [d]\n";

    assert_eq!(
        read_nodejs_streams(0, listing, "").unwrap().offered,
        Vec::new()
    );
}

#[test]
fn a_listing_with_no_nodejs_row_is_an_answer_and_is_empty() {
    assert_eq!(
        read_nodejs_streams(
            0,
            "AlmaLinux 9 - AppStream\nName Stream Profiles Summary\n",
            ""
        )
        .unwrap()
        .offered,
        Vec::new()
    );
}

#[test]
fn a_nodejs_row_too_short_to_hold_a_stream_and_a_profile_refuses_the_read() {
    for (listing, line) in [
        ("nodejs\n", 1),
        ("nodejs 20\n", 1),
        ("nodejs 20 [e]\n", 1),
        ("Name Stream\nnodejs 20 [e] [d]\n", 2),
    ] {
        assert_eq!(
            read_nodejs_streams(0, listing, ""),
            Err(PackageQueryFailure::UnrecognisedLine { line }),
            "{listing}"
        );
    }
}

#[test]
fn a_status_that_is_not_a_listing_refuses_the_read_and_never_reads_as_no_streams() {
    // No cache and no such module both exit 1 and print nothing on standard output.
    for status in [1, 2, 127, -1] {
        assert_eq!(
            read_nodejs_streams(status, RECORDED_CACHED, ""),
            Err(PackageQueryFailure::Unsuccessful { status }),
            "status {status}"
        );
        assert_eq!(
            read_nodejs_streams(status, "", ""),
            Err(PackageQueryFailure::Unsuccessful { status }),
            "status {status}, empty"
        );
    }
}

#[test]
fn a_removal_may_take_what_the_common_profile_installs_and_nothing_else() {
    let allowed = nodejs_module_packages();

    for package in [
        "nodejs",
        "npm",
        "nodejs-libs",
        "nodejs-docs",
        "nodejs-full-i18n",
    ] {
        assert!(allowed.contains(&package), "{package}");
    }
    for package in ["nginx", "httpd", "nodejs-devel", "libbrotli"] {
        assert!(!allowed.contains(&package), "{package}");
    }
}

#[test]
fn sources_without_modules_are_told_apart_from_a_cache_never_built() {
    assert_eq!(
        read_nodejs_streams(1, "", RECORDED_NO_MODULES),
        Err(PackageQueryFailure::NoModules)
    );
    // Inverse controls: the same exit status, other recorded reasons, are "could not ask".
    for stderr in [RECORDED_NO_CACHE, RECORDED_NO_CACHE_EL10, ""] {
        assert_eq!(
            read_nodejs_streams(1, "", stderr),
            Err(PackageQueryFailure::Unsuccessful { status: 1 }),
            "{stderr}"
        );
    }
}

#[test]
fn a_successful_listing_never_reads_standard_error_as_no_modules() {
    // The deprecation warning dnf 4.20 prints is not a refusal.
    assert_eq!(
        read_nodejs_streams(0, RECORDED_CACHED, RECORDED_NO_MODULES)
            .unwrap()
            .offered,
        four_streams()
    );
}

#[test]
fn the_enabled_stream_is_the_one_carrying_the_enabled_marker() {
    assert_eq!(
        read_nodejs_streams(0, RECORDED_INSTALLED, "")
            .unwrap()
            .enabled,
        vec!["20".to_owned()]
    );
}

#[test]
fn a_host_with_the_plain_package_has_no_enabled_stream() {
    // Inverse control of the installed listing: Node.js 16 runs and no row carries `[e]`.
    let listing = read_nodejs_streams(0, RECORDED_UNMANAGED, "").unwrap();

    assert!(listing.enabled.is_empty());
    assert_eq!(listing.offered, four_streams());
}

#[test]
fn an_enabled_stream_outside_the_catalogue_is_still_enabled() {
    let listing = "nodejs 16 [e] common [d] [i], development Javascript runtime\n";
    let read = read_nodejs_streams(0, listing, "").unwrap();

    assert_eq!(read.enabled, vec!["16".to_owned()]);
    assert!(read.offered.is_empty());
}

#[test]
fn the_unmanaged_removal_command_names_the_plain_package() {
    assert_eq!(nodejs_unmanaged_removal_command(), "dnf remove nodejs");
}

#[test]
fn the_stream_release_command_resets_the_module() {
    assert_eq!(nodejs_stream_release_command(), "dnf module reset nodejs");
}
