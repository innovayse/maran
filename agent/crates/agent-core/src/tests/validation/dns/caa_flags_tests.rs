//! Tests for the `caa_flags` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::caa_flags::CaaFlags;
use super::super::caa_flags_error::CaaFlagsError;

#[test]
fn zero_and_128_are_the_only_values() {
    assert_eq!(CaaFlags::parse(0).unwrap().value(), 0);
    assert_eq!(CaaFlags::parse(128).unwrap().value(), 128);
    assert_eq!(CaaFlags::parse(1), Err(CaaFlagsError::Unknown { value: 1 }));
    assert_eq!(
        CaaFlags::parse(256),
        Err(CaaFlagsError::Unknown { value: 256 })
    );
}
