//! Tests for the `zone_file_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::zone_file_name::ZoneFileName;
use super::super::zone_file_name_error::ZoneFileNameError;
use super::super::zone_name::ZoneName;

#[test]
fn a_zone_is_kept_in_its_name_and_db() {
    let zone = ZoneName::parse("example.com").unwrap();

    assert_eq!(ZoneFileName::of(&zone).as_str(), "example.com.db");
}

#[test]
fn the_longest_zone_still_fits_a_file_name() {
    let label = "a".repeat(61);
    let name = format!("{label}.{label}.{label}.{label}.c");
    let zone = ZoneName::parse(&name).unwrap();

    assert!(ZoneFileName::of(&zone).as_str().len() <= 255);
}

#[test]
fn a_file_name_reads_back_to_its_zone() {
    let file = ZoneFileName::parse("example.com.db").unwrap();

    assert_eq!(file.zone().as_str(), "example.com");
}

#[test]
fn a_stray_file_is_not_a_zone() {
    for stray in [
        "example.com",
        "example.com.db.bak",
        ".db",
        "../x.db",
        "a/b.db",
        "com.db",
    ] {
        assert_eq!(
            ZoneFileName::parse(stray),
            Err(ZoneFileNameError::NotAZoneFile),
            "{stray:?}"
        );
    }
}

#[test]
fn a_non_canonical_spelling_is_not_a_zone() {
    assert_eq!(
        ZoneFileName::parse("Example.com.db"),
        Err(ZoneFileNameError::NotCanonical)
    );
    assert_eq!(
        ZoneFileName::parse("example.com..db"),
        Err(ZoneFileNameError::NotCanonical)
    );
}
