//! Tests for the `srv_weight` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::srv_weight::SrvWeight;
use super::super::srv_weight_error::SrvWeightError;

#[test]
fn zero_and_65535_are_accepted_and_65536_is_refused() {
    assert_eq!(SrvWeight::parse(0).unwrap().value(), 0);
    assert_eq!(SrvWeight::parse(65_535).unwrap().value(), 65_535);
    assert_eq!(
        SrvWeight::parse(65_536),
        Err(SrvWeightError::TooLarge { value: 65_536 })
    );
}
