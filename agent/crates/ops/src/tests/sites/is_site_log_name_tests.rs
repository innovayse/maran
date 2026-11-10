//! Tests for which directory entries are one site's log files, live or rotated.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use super::is_site_log_name;

fn domain(name: &str) -> Domain {
    Domain::parse(name).unwrap()
}

#[test]
fn the_live_files_and_every_rotated_generation_of_the_site_are_matched() {
    let site = domain("a.com");

    for name in [
        "a.com.access.log",
        "a.com.error.log",
        "a.com.access.log.1",
        "a.com.access.log.2.gz",
        "a.com.error.log.14.gz",
    ] {
        assert!(is_site_log_name(name, &site), "{name} must be matched");
    }
}

#[test]
fn another_sites_files_are_never_matched_even_when_its_name_extends_this_ones() {
    // The accepting half of the rule above, in the shape that would break a prefix test:
    // `a.com.access` and `a.com.access.log` are valid domains whose files begin with
    // this site's name.
    let site = domain("a.com");

    for name in [
        "b.com.access.log",
        "a.com.access.access.log",
        "a.com.access.access.log.1",
        "a.com.access.log.access.log",
        "a.com.access.log.access.log.2.gz",
        "xa.com.access.log",
        "a.com.access.log.bak",
        "a.com.access.log.0",
        "a.com.other.log",
        "a.com",
    ] {
        assert!(!is_site_log_name(name, &site), "{name} must not be matched");
    }
}
