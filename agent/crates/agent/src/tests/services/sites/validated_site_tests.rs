//! What the strict site check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_site;
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
