//! What the cleanup site check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_existing_site;
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
fn a_reserved_domain_and_a_reserved_alias_are_accepted_so_the_site_can_be_wound_down() {
    let spec = a_static_spec(&["203.0.113.7", "www.x.local"]);

    let input = validated_existing_site("acme", "x.local", Some(&spec)).expect("accepted");

    assert_eq!(input.domain.as_str(), "x.local");
    assert_eq!(input.aliases.len(), 2);
    assert_eq!(input.aliases[0].as_str(), "203.0.113.7");
}

#[test]
fn an_ordinary_domain_and_alias_are_accepted() {
    let spec = a_static_spec(&["www.example.com"]);

    let input = validated_existing_site("acme", "example.com", Some(&spec)).expect("accepted");

    assert_eq!(input.domain.as_str(), "example.com");
}

#[test]
fn a_malformed_alias_is_still_refused_as_invalid_input() {
    let spec = a_static_spec(&["bad_name.example.com"]);

    let error = validated_existing_site("acme", "example.com", Some(&spec))
        .map(|_| ())
        .expect_err("the shape checks still apply");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_malformed_domain_is_still_refused() {
    let spec = a_static_spec(&[]);

    assert!(validated_existing_site("acme", "bad_name.example.com", Some(&spec)).is_err());
}

#[test]
fn an_absent_spec_is_refused() {
    assert!(validated_existing_site("acme", "example.com", None).is_err());
}
