//! Tests for `list_zone_files`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, directories, signed_directory, zone_directory};
use super::list_zone_files;

#[test]
fn zones_are_listed_sorted_with_what_the_daemon_serves() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "zeta.example", 1, "; x\n")
        .with_unserved_file(&zone_directory(), "alpha.example");

    let listed = list_zone_files(&host, distro(), &directories()).unwrap();

    let summary: Vec<(&str, bool)> = listed.iter().map(|l| (l.zone.as_str(), l.served)).collect();
    assert_eq!(
        summary,
        vec![("alpha.example", false), ("zeta.example", true)]
    );
}

#[test]
fn an_entry_that_is_not_a_zone_file_is_skipped() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 1, "; x\n")
        .with_stray_entry("example.com.db.bak")
        .with_stray_entry("Example.com.db")
        .with_stray_entry(".maran-safe-write-abc123")
        .with_stray_entry("notes.txt");

    let listed = list_zone_files(&host, distro(), &directories()).unwrap();

    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0].zone.as_str(), "example.com");
}

#[test]
fn a_zone_in_the_signed_directory_is_listed_once_beside_the_plain_ones() {
    let host = FakeZoneHost::new()
        .with_zone(&signed_directory(), "signed.example", 1, "; x\n")
        .with_unserved_file(&zone_directory(), "plain.example");

    let listed = list_zone_files(&host, distro(), &directories()).unwrap();

    let summary: Vec<(&str, bool)> = listed.iter().map(|l| (l.zone.as_str(), l.served)).collect();
    assert_eq!(
        summary,
        vec![("plain.example", false), ("signed.example", true)]
    );
}

#[test]
fn a_zone_with_a_file_in_both_directories_is_listed_once() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "both.example")
        .with_unserved_file(&signed_directory(), "both.example");

    assert_eq!(
        list_zone_files(&host, distro(), &directories())
            .unwrap()
            .len(),
        1
    );
}
