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

/// openSUSE Leap 16.0 maps to the SUSE family, with `ID` and `VERSION_ID` quoted exactly as
/// os-release prints them.
///
/// Constructed from the os-release format's documentation, not recorded from a Leap host: the
/// recording is owed to the first SUSE polygon run (`cat /etc/os-release` in the pinned image).
#[test]
fn quoted_opensuse_leap_16_0_maps_to_the_suse_family() {
    let info =
        parse("NAME=\"openSUSE Leap\"\nID=\"opensuse-leap\"\nVERSION_ID=\"16.0\"\n").unwrap();

    assert_eq!(info.id, "opensuse-leap");
    assert_eq!(info.family, DistroFamily::Suse);
    assert_eq!(info.version_id, "16.0");
}

/// openSUSE Leap 16.1 maps to the SUSE family too: the point release is what a host reports, and
/// both pairs the installer's matrix admits must parse.
#[test]
fn quoted_opensuse_leap_16_1_maps_to_the_suse_family() {
    let info = parse("ID=\"opensuse-leap\"\nVERSION_ID=\"16.1\"\n").unwrap();

    assert_eq!(info.id, "opensuse-leap");
    assert_eq!(info.family, DistroFamily::Suse);
    assert_eq!(info.version_id, "16.1");
}

/// SLES 16.0 maps to the SUSE family: it is claimed through its community sibling Leap 16, the
/// same base, exactly as the RHEL family is claimed through its rebuilds.
#[test]
fn quoted_sles_16_0_maps_to_the_suse_family() {
    let info = parse("ID=\"sles\"\nVERSION_ID=\"16.0\"\n").unwrap();

    assert_eq!(info.id, "sles");
    assert_eq!(info.family, DistroFamily::Suse);
    assert_eq!(info.version_id, "16.0");
}

/// openSUSE Tumbleweed is refused although its os-release says `ID_LIKE="suse opensuse"`.
///
/// The installer resolves a family from `ID_LIKE` and so would admit it, which is why preflight
/// judges the concrete id and version pair and this match refuses every id it does not list. A
/// rolling release has no `VERSION_ID` to pin a matrix row to, and nothing about its package set
/// was ever read. The line is part of the input so the test fails if a future edit starts
/// reading `ID_LIKE`.
#[test]
fn opensuse_tumbleweed_is_refused_although_id_like_says_suse() {
    assert_eq!(
        parse("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"\nVERSION_ID=\"20261101\"\n"),
        Err(DetectError::Unsupported {
            id: "opensuse-tumbleweed".into()
        })
    );
}

/// Every id that calls itself SUSE-like, or that a nearby family could be mistaken for, is
/// refused with its own id carried, so the operator learns which host they pointed the
/// installer at.
///
/// The inverse control is the last case: a supported id with the same shape still parses, so a
/// match that refused everything would fail here.
#[test]
fn unsupported_ids_are_refused_with_their_id_and_a_supported_id_still_parses() {
    for id in [
        "opensuse-microos",
        "sled",
        "sles_sap",
        "fedora",
        "alpine",
        "centos",
    ] {
        assert_eq!(
            parse(&format!("ID=\"{id}\"\nVERSION_ID=\"16.0\"\n")),
            Err(DetectError::Unsupported { id: id.into() }),
            "{id}"
        );
    }

    assert!(parse("ID=\"opensuse-leap\"\nVERSION_ID=\"16.0\"\n").is_ok());
}
