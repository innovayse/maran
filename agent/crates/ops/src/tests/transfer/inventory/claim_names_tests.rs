//! Tests for [`claim_names`]: validating the host names of one virtual host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::claim_names::claim_names;

/// Turns string literals into the owned names the function takes.
fn names(raw: &[&str]) -> Vec<String> {
    raw.iter().map(|name| (*name).to_owned()).collect()
}

/// The first valid name is the primary and the rest are aliases, lowercased.
#[test]
fn the_first_valid_name_is_the_primary() {
    let claimed = claim_names(&names(&["Shop.Example.com", "www.shop.example.com"]));

    assert_eq!(claimed.primary.unwrap().as_str(), "shop.example.com");
    assert_eq!(claimed.aliases.len(), 1);
    assert_eq!(claimed.aliases[0].as_str(), "www.shop.example.com");
    assert_eq!(claimed.refused, 0);
}

/// A repeat of the primary or of an alias is dropped.
#[test]
fn repeated_names_are_dropped() {
    let claimed = claim_names(&names(&[
        "a.example.com",
        "A.example.com",
        "b.example.com",
        "b.example.com",
    ]));

    assert_eq!(claimed.aliases.len(), 1);
}

/// Wildcards, patterns, addresses and reserved names are refused and counted,
/// never repaired.
#[test]
fn wildcards_patterns_addresses_and_reserved_names_are_refused() {
    let claimed = claim_names(&names(&[
        "*.example.com",
        "~^www\\d+\\.example\\.com$",
        "192.0.2.10",
        "localhost",
        "host.internal",
        "good.example.com",
    ]));

    assert_eq!(claimed.primary.unwrap().as_str(), "good.example.com");
    assert_eq!(claimed.refused, 5);
}

/// The catch-all and the empty name are conventions: skipped without a count.
#[test]
fn the_catch_all_is_skipped_without_a_refusal() {
    let claimed = claim_names(&names(&["_", ""]));

    assert!(claimed.primary.is_none());
    assert_eq!(claimed.refused, 0);
}

/// A leading dot means the name and its subdomains; the name is claimed.
#[test]
fn a_leading_dot_is_dropped() {
    let claimed = claim_names(&names(&[".example.org"]));

    assert_eq!(claimed.primary.unwrap().as_str(), "example.org");
}
