//! Tests for how the RHEL family reads which packages its repositories offer.
//!
//! The outputs are what `dnf -q -C list --available` printed in an
//! `almalinux:9` container, recorded 2026-11-09 into
//! `tests/fixtures/package_manager/`: after `dnf makecache` with one name that
//! matches (`php-fpm`, standing in for a Remi package, since the stock image
//! has no Remi repository) and one that does not (exit 0); with no name
//! matching (exit 1, on standard error); and with no cache at all (exit 1, on
//! standard error). Standard output is empty in the last two.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{package_candidate_arguments, package_candidate_binary, read_package_candidates};
use crate::PackageQueryFailure;

/// One of two names matched.
const MIXED: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_list_available_mixed.txt");

/// What dnf wrote to standard error when no name matched.
const NO_MATCH: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_list_available_no_match.txt");

/// What dnf wrote to standard error when the cache was never built.
const NO_CACHE: &str =
    include_str!("../../../tests/fixtures/package_manager/dnf_list_available_no_cache.txt");

/// The two names the recording asked about.
fn asked() -> Vec<String> {
    vec!["php-fpm".to_owned(), "php83-php-fpm".to_owned()]
}

#[test]
fn only_the_name_dnf_listed_is_offered() {
    let offered = read_package_candidates(0, MIXED, "", &asked()).unwrap();

    assert_eq!(offered, vec!["php-fpm".to_owned()]);
}

#[test]
fn a_row_for_a_name_that_was_not_asked_about_is_never_offered() {
    let offered = read_package_candidates(0, MIXED, "", &["php83-php-fpm".to_owned()]).unwrap();

    assert!(offered.is_empty());
}

#[test]
fn no_matching_package_is_an_answer_and_offers_nothing() {
    let offered = read_package_candidates(1, "", NO_MATCH, &asked()).unwrap();

    assert!(offered.is_empty());
}

#[test]
fn a_cache_that_was_never_built_is_a_failure_and_never_an_empty_list() {
    // Inverse control to the test above: the same status 1 and empty standard
    // output, a different sentence, the opposite verdict.
    assert_eq!(
        read_package_candidates(1, "", NO_CACHE, &asked()),
        Err(PackageQueryFailure::Unsuccessful { status: 1 })
    );
}

#[test]
fn a_row_with_too_few_columns_fails_the_read_with_its_number() {
    let output = format!("{MIXED}php83-php-fpm.x86_64\n");
    let line = output.lines().count();

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn the_query_is_a_cache_only_available_listing_with_the_operands_after_the_separator() {
    assert_eq!(package_candidate_binary().to_string(), "/usr/bin/dnf");
    let arguments = package_candidate_arguments(&asked());

    assert!(arguments.contains(&"-C".to_owned()));
    assert_eq!(arguments[arguments.len() - 3], "--");
}
