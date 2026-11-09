//! Tests for the `dns_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::dns_name::DnsName;
use super::super::dns_name_error::DnsNameError;

#[test]
fn a_target_parses_lowercased_and_without_its_trailing_dot() {
    let name = DnsName::parse("Mail.Example.com.").unwrap();

    assert_eq!(name.as_str(), "mail.example.com");
    assert_eq!(name.fully_qualified(), "mail.example.com.");
}

#[test]
fn a_service_style_target_with_an_underscore_parses() {
    assert!(DnsName::parse("sel1._domainkey.provider.net").is_ok());
}

#[test]
fn an_address_typed_where_a_name_belongs_is_refused() {
    assert_eq!(
        DnsName::parse("192.0.2.1"),
        Err(DnsNameError::NumericFinalLabel)
    );
}

#[test]
fn a_single_label_and_an_empty_name_are_refused() {
    assert_eq!(DnsName::parse("mail"), Err(DnsNameError::SingleLabel));
    assert_eq!(DnsName::parse(""), Err(DnsNameError::Empty));
    assert_eq!(DnsName::parse("."), Err(DnsNameError::Empty));
}

#[test]
fn a_wildcard_is_not_a_target() {
    assert!(matches!(
        DnsName::parse("*.example.com"),
        Err(DnsNameError::IllegalCharacter { character: '*' })
    ));
}

#[test]
fn every_injection_attempt_is_refused() {
    for hostile in [
        "a.example.com\nIN A 1.2.3.4",
        "a.example.com\r",
        "a.example.com\0",
        "a.\"example.com",
        "a.example\\.com",
        "$INCLUDE.example.com",
        "a.example.com;x",
        "a/b.example.com",
        "a..example.com",
        "a b.example.com",
    ] {
        assert!(DnsName::parse(hostile).is_err(), "{hostile:?} was accepted");
    }
}

#[test]
fn a_label_of_64_characters_and_a_name_of_254_are_refused() {
    assert!(matches!(
        DnsName::parse(&format!("{}.com", "a".repeat(64))),
        Err(DnsNameError::InvalidLabel { .. })
    ));
    let long = vec!["a".repeat(50); 5].join(".") + ".com";
    assert_eq!(DnsName::parse(&long), Err(DnsNameError::TooLong));
}
