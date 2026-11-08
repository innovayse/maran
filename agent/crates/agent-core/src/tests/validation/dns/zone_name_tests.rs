//! Tests for the `zone_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::zone_name::ZoneName;
use super::super::zone_name_error::ZoneNameError;

#[test]
fn an_ordinary_zone_parses_and_is_lowercased() {
    assert_eq!(
        ZoneName::parse("Example.COM").unwrap().as_str(),
        "example.com"
    );
}

#[test]
fn a_trailing_dot_is_dropped() {
    let zone = ZoneName::parse("example.com.").unwrap();

    assert_eq!(zone.as_str(), "example.com");
    assert_eq!(zone.fully_qualified(), "example.com.");
}

#[test]
fn a_single_label_is_refused() {
    assert_eq!(ZoneName::parse("com"), Err(ZoneNameError::SingleLabel));
}

#[test]
fn an_empty_name_and_a_lone_dot_are_refused() {
    assert_eq!(ZoneName::parse(""), Err(ZoneNameError::Empty));
    assert_eq!(ZoneName::parse("."), Err(ZoneNameError::Empty));
}

#[test]
fn a_name_too_long_to_be_a_file_stem_is_refused() {
    let label = "a".repeat(62);
    let name = format!("{label}.{label}.{label}.{label}.com");

    assert!(name.len() > ZoneName::MAX_LENGTH);
    assert_eq!(ZoneName::parse(&name), Err(ZoneNameError::TooLong));
}

#[test]
fn every_injection_attempt_is_refused() {
    for hostile in [
        "example.com\nIN A 1.2.3.4",
        "example.com\r\n",
        "example.com\0",
        "exa\"mple.com",
        "exa\\mple.com",
        "$INCLUDE /etc/passwd",
        "example.com;",
        "../example.com",
        "a/b.example.com",
        "exa..mple.com",
        "a b.com",
    ] {
        assert!(
            ZoneName::parse(hostile).is_err(),
            "{hostile:?} was accepted"
        );
    }
}

#[test]
fn a_reserved_or_address_shaped_name_is_refused() {
    assert!(matches!(
        ZoneName::parse("localhost.localdomain"),
        Err(ZoneNameError::NotAHostName(_))
    ));
    assert!(matches!(
        ZoneName::parse("192.0.2.1"),
        Err(ZoneNameError::NotAHostName(_))
    ));
}
