//! Tests for the `reserved_host_name` module.
//!
//! The predicate receives names the shape check already passed, so every input
//! here is lower-case and well-formed; case folding is `Domain`'s job and is
//! tested in `domain_tests.rs`.

use super::reservation_of;
use crate::validation::web::domain_error::DomainError;

#[test]
fn an_ipv4_literal_is_refused_as_a_numeric_final_label() {
    assert_eq!(
        reservation_of("203.0.113.7"),
        Some(DomainError::NumericFinalLabel)
    );
}

#[test]
fn only_the_final_label_decides_whether_a_name_is_numeric() {
    assert_eq!(
        reservation_of("shop.123"),
        Some(DomainError::NumericFinalLabel)
    );
    assert_eq!(reservation_of("123.example.com"), None);
    assert_eq!(reservation_of("shop.1a"), None);
}

#[test]
fn a_numeric_name_that_is_also_special_use_reports_the_numeric_reason_first() {
    // Fixes the documented order: numeric, then localhost, then special-use.
    assert_eq!(
        reservation_of("localhost.1"),
        Some(DomainError::NumericFinalLabel)
    );
}

#[test]
fn localhost_and_its_subdomains_are_refused() {
    assert_eq!(
        reservation_of("localhost"),
        Some(DomainError::LocalhostFamily)
    );
    assert_eq!(
        reservation_of("a.localhost"),
        Some(DomainError::LocalhostFamily)
    );
    assert_eq!(reservation_of("notlocalhost"), None);
    assert_eq!(reservation_of("localhost.example.com"), None);
}

#[test]
fn a_special_use_suffix_is_refused_only_on_a_label_boundary() {
    assert_eq!(
        reservation_of("printer.home.arpa"),
        Some(DomainError::SpecialUseSuffix)
    );
    assert_eq!(
        reservation_of("home.arpa"),
        Some(DomainError::SpecialUseSuffix)
    );
    assert_eq!(reservation_of("shop.notlocal"), None);
    assert_eq!(reservation_of("shop.arpa"), None);
}

#[test]
fn every_special_use_suffix_is_refused_alone_and_as_a_parent() {
    for suffix in [
        "local",
        "localdomain",
        "internal",
        "lan",
        "home.arpa",
        "invalid",
    ] {
        assert_eq!(
            reservation_of(suffix),
            Some(DomainError::SpecialUseSuffix),
            "{suffix} itself"
        );
        assert_eq!(
            reservation_of(&format!("shop.{suffix}")),
            Some(DomainError::SpecialUseSuffix),
            "shop.{suffix}"
        );
    }
}

#[test]
fn the_documentation_suffixes_test_and_example_are_not_reserved() {
    assert_eq!(reservation_of("shop.test"), None);
    assert_eq!(reservation_of("shop.example"), None);
}

#[test]
fn an_ordinary_name_is_not_reserved() {
    assert_eq!(reservation_of("shop.example.com"), None);
    assert_eq!(reservation_of("shop"), None);
}
