//! Tests for the `soa_interval` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::soa_interval::SoaInterval;
use super::super::soa_interval_error::SoaIntervalError;

#[test]
fn the_bounds_are_accepted_and_the_outside_is_refused() {
    assert!(SoaInterval::parse(60).is_ok());
    assert_eq!(SoaInterval::parse(2_419_200).unwrap().seconds(), 2_419_200);
    assert_eq!(
        SoaInterval::parse(0),
        Err(SoaIntervalError::TooSmall { value: 0 })
    );
    assert_eq!(
        SoaInterval::parse(2_419_201),
        Err(SoaIntervalError::TooLarge { value: 2_419_201 })
    );
}
