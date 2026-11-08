//! Tests for the `send_limit_per_hour` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, SendLimitPerHour};
use crate::validation::mail::send_limit_per_hour_error::SendLimitPerHourError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(SendLimitPerHour::parse(1).unwrap().value(), 1);
    assert_eq!(SendLimitPerHour::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(SendLimitPerHour::parse(0), Err(SendLimitPerHourError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        SendLimitPerHour::parse(MAXIMUM + 1),
        Err(SendLimitPerHourError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}
