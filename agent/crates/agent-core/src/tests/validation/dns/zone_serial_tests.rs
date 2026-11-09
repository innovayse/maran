//! Tests for the `zone_serial` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::zone_serial::ZoneSerial;
use super::super::zone_serial_error::ZoneSerialError;

#[test]
fn zero_is_refused_and_anything_else_is_kept() {
    assert_eq!(ZoneSerial::parse(0), Err(ZoneSerialError::Zero));
    assert_eq!(
        ZoneSerial::parse(2_026_110_701).unwrap().value(),
        2_026_110_701
    );
    assert_eq!(ZoneSerial::parse(u32::MAX).unwrap().value(), u32::MAX);
}
