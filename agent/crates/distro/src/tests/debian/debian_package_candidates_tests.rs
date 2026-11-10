//! Tests for how the Debian family reads which packages its sources offer.
//!
//! The outputs are what `apt-cache policy` printed for the six php-fpm package
//! names: on a stock `ubuntu:24.04` container after `apt-get update` (only
//! `php8.3-fpm` is carried, and `php7.4-fpm` is a known name with
//! `Candidate: (none)`), on an Ubuntu 24.04 host that also has the Sury
//! repository (all six), and on a container whose lists were never built
//! (nothing at all). Recorded 2026-11-09 into
//! `tests/fixtures/package_manager/`; exit 0 in all three.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{package_candidate_arguments, package_candidate_binary, read_package_candidates};
use crate::PackageQueryFailure;

/// A stock Ubuntu 24.04 after `apt-get update`: only php8.3 is carried.
const UBUNTU_ONLY: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_policy_ubuntu_php83_only.txt");

/// An Ubuntu 24.04 host with the Sury repository: every version is carried.
const SURY: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_policy_sury_all_versions.txt");

/// A container whose lists directory was never built: the program prints nothing.
const EMPTY_LISTS: &str =
    include_str!("../../../tests/fixtures/package_manager/apt_policy_empty_lists.txt");

/// The six names the PHP screen asks about, oldest first.
fn asked() -> Vec<String> {
    ["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"]
        .iter()
        .map(|version| format!("php{version}-fpm"))
        .collect()
}

#[test]
fn a_host_carrying_only_php_8_3_offers_only_php_8_3() {
    let offered = read_package_candidates(0, UBUNTU_ONLY, &asked()).unwrap();

    assert_eq!(offered, vec!["php8.3-fpm".to_owned()]);
}

#[test]
fn a_known_name_with_no_candidate_is_not_offered() {
    // php7.4-fpm has a header in the recording and `Candidate: (none)`.
    assert!(UBUNTU_ONLY.contains("php7.4-fpm:"));

    let offered = read_package_candidates(0, UBUNTU_ONLY, &asked()).unwrap();

    assert!(!offered.contains(&"php7.4-fpm".to_owned()));
}

#[test]
fn a_host_carrying_every_version_offers_all_six_in_the_order_asked() {
    // Inverse control: the reader is not mutated to refuse everything.
    let offered = read_package_candidates(0, SURY, &asked()).unwrap();

    assert_eq!(offered, asked());
}

#[test]
fn a_name_that_was_not_asked_about_is_never_offered() {
    let only_eight_three = vec!["php8.3-fpm".to_owned()];

    let offered = read_package_candidates(0, SURY, &only_eight_three).unwrap();

    assert_eq!(offered, only_eight_three);
}

#[test]
fn output_with_no_package_at_all_offers_nothing() {
    assert_eq!(
        read_package_candidates(0, EMPTY_LISTS, &asked()).unwrap(),
        Vec::<String>::new()
    );
}

#[test]
fn a_failing_status_is_a_failure_and_never_an_empty_list() {
    assert_eq!(
        read_package_candidates(100, "", &asked()),
        Err(PackageQueryFailure::Unsuccessful { status: 100 })
    );
}

#[test]
fn a_line_outside_the_grammar_fails_the_read_with_its_number() {
    let output = format!("{UBUNTU_ONLY}this is not apt-cache policy output\n");
    let line = output.lines().count();

    assert_eq!(
        read_package_candidates(0, &output, &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn the_query_is_apt_cache_policy_with_the_operands_after_the_separator() {
    assert_eq!(package_candidate_binary().to_string(), "/usr/bin/apt-cache");
    assert_eq!(
        package_candidate_arguments(&asked())[..3],
        [
            "policy".to_owned(),
            "--".to_owned(),
            "php7.4-fpm".to_owned()
        ]
    );
}
