//! Tests for the `os_release` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `os_release.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse;
use crate::detection::detect_error::DetectError;
use crate::family::DistroFamily;

#[test]
fn unquoted_ubuntu_id_maps_to_the_debian_family() {
    let info = parse("NAME=\"Ubuntu\"\nID=ubuntu\nVERSION_ID=\"24.04\"\n").unwrap();

    assert_eq!(info.id, "ubuntu");
    assert_eq!(info.family, DistroFamily::Debian);
    assert_eq!(info.version_id, "24.04");
}

#[test]
fn unquoted_debian_id_maps_to_the_debian_family() {
    let info = parse("ID=debian\nVERSION_ID=\"12\"\n").unwrap();

    assert_eq!(info.id, "debian");
    assert_eq!(info.family, DistroFamily::Debian);
    assert_eq!(info.version_id, "12");
}

#[test]
fn quoted_almalinux_id_maps_to_the_rhel_family() {
    let info = parse("ID=\"almalinux\"\nVERSION_ID=\"9.4\"\n").unwrap();

    assert_eq!(info.id, "almalinux");
    assert_eq!(info.family, DistroFamily::Rhel);
    assert_eq!(info.version_id, "9.4");
}

#[test]
fn quoted_rocky_id_maps_to_the_rhel_family() {
    let info = parse("ID=\"rocky\"\nVERSION_ID=9.4\n").unwrap();

    assert_eq!(info.id, "rocky");
    assert_eq!(info.family, DistroFamily::Rhel);
    assert_eq!(info.version_id, "9.4");
}

#[test]
fn unsupported_distribution_is_refused_by_id() {
    assert_eq!(
        parse("ID=alpine\nVERSION_ID=\"3.20\"\n"),
        Err(DetectError::Unsupported {
            id: "alpine".into()
        })
    );
}

#[test]
fn content_without_an_id_line_is_refused_rather_than_defaulted() {
    assert_eq!(
        parse("NAME=\"Some Linux\"\nVERSION_ID=\"1\"\n"),
        Err(DetectError::Unsupported { id: String::new() })
    );
}
