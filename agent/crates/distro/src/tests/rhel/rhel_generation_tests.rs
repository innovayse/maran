//! Tests for [`RhelGeneration`]: a release is read as a version, not as a string.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RhelGeneration;

#[test]
fn the_9_releases_are_the_9_generation() {
    for version_id in ["9", "9.0", "9.4", "9.8"] {
        assert_eq!(
            RhelGeneration::of(version_id),
            RhelGeneration::El9,
            "{version_id}"
        );
    }
}

#[test]
fn the_10_releases_and_newer_are_the_10_generation() {
    // `10.2` is the release the 10 generation's fixtures were recorded on; the major is read as a
    // whole number, so a longer one is compared as what it says.
    for version_id in ["10", "10.0", "10.2", "11.0", "100.1"] {
        assert_eq!(
            RhelGeneration::of(version_id),
            RhelGeneration::El10,
            "{version_id}"
        );
    }
}

#[test]
fn a_release_that_is_not_a_number_is_read_as_the_generation_the_adapter_already_knew() {
    // Inverse control: nothing here may select the 10 generation by accident (a name that merely
    // contains a 10, a minor of 10 under major 9), so the answer for an unreadable release is
    // the established one.
    for version_id in ["", ".", "x", "9.10", "ten", "-10", "1O"] {
        assert_eq!(
            RhelGeneration::of(version_id),
            RhelGeneration::El9,
            "{version_id}"
        );
    }
}
