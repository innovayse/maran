//! Tests for the `record_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::record_name::RecordName;
use super::super::record_name_error::RecordNameError;
use super::super::zone_name::ZoneName;

#[test]
fn at_is_the_apex() {
    assert_eq!(RecordName::parse("@").unwrap(), RecordName::Apex);
    assert_eq!(RecordName::Apex.owner_text(), "@");
    assert!(RecordName::Apex.is_apex());
}

#[test]
fn ordinary_wildcard_and_service_names_parse() {
    for ok in [
        "www",
        "a.b.c",
        "*",
        "*.shop",
        "_dmarc",
        "_sip._tcp",
        "sel1._domainkey",
        "WWW",
    ] {
        assert!(RecordName::parse(ok).is_ok(), "{ok:?}");
    }
    assert_eq!(RecordName::parse("WWW").unwrap().owner_text(), "www");
}

#[test]
fn a_wildcard_anywhere_but_the_leftmost_label_is_refused() {
    for bad in ["a.*", "w*", "*w", "a.*.b", "**"] {
        assert_eq!(
            RecordName::parse(bad),
            Err(RecordNameError::MisplacedWildcard),
            "{bad:?}"
        );
    }
}

#[test]
fn an_absolute_name_is_refused() {
    assert_eq!(
        RecordName::parse("www.example.com."),
        Err(RecordNameError::Absolute)
    );
}

#[test]
fn every_injection_attempt_is_refused() {
    for hostile in [
        "www\nIN A 1.2.3.4",
        "www\r",
        "www\0",
        "w\"w",
        "w\\w",
        "$INCLUDE",
        "$GENERATE",
        "www;comment",
        "a/b",
        "a..b",
        ".a",
        "-a",
        "a-",
        "a b",
    ] {
        assert!(
            RecordName::parse(hostile).is_err(),
            "{hostile:?} was accepted"
        );
    }
}

#[test]
fn a_label_of_64_characters_is_refused_and_63_is_accepted() {
    assert!(RecordName::parse(&"a".repeat(63)).is_ok());
    assert!(matches!(
        RecordName::parse(&"a".repeat(64)),
        Err(RecordNameError::InvalidLabel { .. })
    ));
}

#[test]
fn a_name_of_300_characters_is_refused() {
    let name = vec!["a".repeat(50); 6].join(".");

    assert_eq!(RecordName::parse(&name), Err(RecordNameError::TooLong));
}

#[test]
fn a_name_that_overflows_once_the_zone_is_appended_is_refused() {
    let zone = ZoneName::parse("example.com").unwrap();
    let name = RecordName::parse(&vec!["a".repeat(60); 4].join(".")).unwrap();

    assert_eq!(name.fits_within(&zone), Err(RecordNameError::TooLongInZone));
    assert!(RecordName::parse("www").unwrap().fits_within(&zone).is_ok());
}
