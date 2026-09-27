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

/// Oracle Linux reports `ID=ol`, which this detection refused while the INSTALLER accepted it
/// through `ID_LIKE=fedora` — so the panel installed and the root daemon then died on every start
/// with `unsupported distro: ol`, measured on Oracle Linux 8, 9 and 10 (issue #54).
///
/// The version is the real `VERSION_ID` from an Oracle Linux 9 image (`9.8`) rather than a round
/// `9`, because the two lists are compared by id and the point release is what a host actually
/// reports.
#[test]
fn oracle_linux_id_maps_to_the_rhel_family() {
    let info = parse("ID=\"ol\"\nVERSION_ID=\"9.8\"\n").unwrap();

    assert_eq!(info.id, "ol");
    assert_eq!(info.family, DistroFamily::Rhel);
    assert_eq!(info.version_id, "9.8");
}

/// RHEL itself, which the install polygon cannot exercise — RHEL's own images carry no nginx or
/// PostgreSQL without a subscription, so the family is proved through its rebuilds. That is exactly
/// why it needs a test here: it is the one supported vendor with no run to catch its absence.
#[test]
fn rhel_id_maps_to_the_rhel_family() {
    let info = parse("ID=\"rhel\"\nVERSION_ID=\"9.4\"\n").unwrap();

    assert_eq!(info.id, "rhel");
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
