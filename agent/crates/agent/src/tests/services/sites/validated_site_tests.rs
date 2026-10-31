//! What the strict site check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CERTIFICATE_NAME_LIMIT, MAXIMUM_ALIASES, validated_site};
use crate::proto::{ErrorCode, SiteBackendType, SiteSpec};

/// A static spec carrying `aliases`.
fn a_static_spec(aliases: &[&str]) -> SiteSpec {
    SiteSpec {
        aliases: aliases.iter().map(|alias| (*alias).to_owned()).collect(),
        backend_type: SiteBackendType::Static as i32,
        php_version: String::new(),
        proxy_upstream: String::new(),
        has_certificate: false,
    }
}

#[test]
fn an_ordinary_domain_and_alias_are_accepted() {
    let spec = a_static_spec(&["www.example.com"]);

    let input = validated_site("acme", "example.com", Some(&spec)).expect("accepted");

    assert_eq!(input.domain.as_str(), "example.com");
    assert_eq!(input.aliases.len(), 1);
    assert_eq!(input.aliases[0].as_str(), "www.example.com");
}

#[test]
fn a_reserved_domain_is_refused_as_invalid_input() {
    let spec = a_static_spec(&[]);

    for reserved in ["203.0.113.7", "localhost", "x.local"] {
        let error = validated_site("acme", reserved, Some(&spec))
            .map(|_| ())
            .expect_err("a reserved domain must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_reserved_alias_is_refused_even_when_the_domain_is_ordinary() {
    for reserved in ["203.0.113.7", "x.local", "localhost"] {
        let spec = a_static_spec(&["www.example.com", reserved]);

        let error = validated_site("acme", "example.com", Some(&spec))
            .map(|_| ())
            .expect_err("a reserved alias must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_malformed_alias_is_still_refused() {
    let spec = a_static_spec(&["bad_name.example.com"]);

    assert!(validated_site("acme", "example.com", Some(&spec)).is_err());
}

#[test]
fn an_absent_spec_is_refused() {
    assert!(validated_site("acme", "example.com", None).is_err());
}

/// A static spec carrying `count` distinct, well-formed aliases of `example.com`.
fn a_static_spec_with_aliases(count: usize) -> SiteSpec {
    let aliases: Vec<String> = (0..count)
        .map(|index| format!("alias{index}.example.com"))
        .collect();
    let borrowed: Vec<&str> = aliases.iter().map(String::as_str).collect();
    a_static_spec(&borrowed)
}

#[test]
fn a_site_carrying_exactly_the_maximum_number_of_aliases_is_accepted() {
    let spec = a_static_spec_with_aliases(MAXIMUM_ALIASES);

    let input = validated_site("acme", "example.com", Some(&spec)).expect("accepted");

    assert_eq!(input.aliases.len(), MAXIMUM_ALIASES);
}

#[test]
fn one_alias_over_the_maximum_is_refused_as_invalid_input() {
    let spec = a_static_spec_with_aliases(MAXIMUM_ALIASES + 1);

    let error = validated_site("acme", "example.com", Some(&spec))
        .map(|_| ())
        .expect_err("one alias over the maximum must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_alias_maximum_leaves_the_primary_domain_one_name_of_a_certificate() {
    assert_eq!(MAXIMUM_ALIASES + 1, CERTIFICATE_NAME_LIMIT);
}
