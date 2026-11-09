//! Tests for which directory entries are a site's rotated logs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use super::rotated_log_name;

fn domain(name: &str) -> Domain {
    Domain::parse(name).unwrap()
}

#[test]
fn every_spelling_logrotate_writes_is_matched_with_its_generation() {
    let site = domain("a.com");

    assert_eq!(rotated_log_name("a.com.access.log.1", &site), Some(1));
    assert_eq!(rotated_log_name("a.com.access.log.2.gz", &site), Some(2));
    assert_eq!(rotated_log_name("a.com.error.log.3", &site), Some(3));
    assert_eq!(rotated_log_name("a.com.error.log.14.gz", &site), Some(14));
    assert_eq!(rotated_log_name("a.com.access.log.15.gz", &site), Some(15));
}

#[test]
fn the_live_file_and_generation_zero_are_never_matched() {
    let site = domain("a.com");

    assert_eq!(rotated_log_name("a.com.access.log", &site), None);
    assert_eq!(rotated_log_name("a.com.error.log", &site), None);
    assert_eq!(rotated_log_name("a.com.access.log.0", &site), None);
    assert_eq!(rotated_log_name("a.com.access.log.0.gz", &site), None);
}

#[test]
fn a_name_that_is_not_exactly_the_pattern_is_left_alone() {
    let site = domain("a.com");

    for look_alike in [
        "a.com.access.log.1.bak",
        "a.com.access.log.x",
        "a.com.access.log.1x",
        "a.com.access.log.01",
        "a.com.access.log.",
        "a.com.access.log..gz",
        "a.com.access.log.1.gz.gz",
        "a.com.access.log.-1",
        "a.com.access.log.+1",
        "a.com.access.log.1 ",
        "a.com.access.log.1234567890",
        "a.com.other.log.1",
        "a.com.access.log1",
        "xa.com.access.log.1",
        "a.com.conf",
        "notes.txt",
        "",
    ] {
        assert_eq!(rotated_log_name(look_alike, &site), None, "{look_alike:?}");
    }
}

#[test]
fn a_site_whose_name_extends_another_sites_name_is_not_matched_by_it() {
    let short = domain("a.com");

    // Files of the site `a.com.access` and of the site `a.com.access.log`.
    assert_eq!(rotated_log_name("a.com.access.access.log.1", &short), None);
    assert_eq!(
        rotated_log_name("a.com.access.error.log.2.gz", &short),
        None
    );
    assert_eq!(
        rotated_log_name("a.com.access.log.access.log.1", &short),
        None
    );
    assert_eq!(
        rotated_log_name("a.com.access.log.error.log.1.gz", &short),
        None
    );
}

#[test]
fn the_longer_site_matches_only_its_own_files() {
    let longer = domain("a.com.access");

    assert_eq!(
        rotated_log_name("a.com.access.access.log.1", &longer),
        Some(1)
    );
    assert_eq!(rotated_log_name("a.com.access.log.1", &longer), None);
}
