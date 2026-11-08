//! Tests for the `request_rate` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_BURST, MAXIMUM_PER_SECOND, RequestRate, RequestRateError};

#[test]
fn the_ends_of_both_ranges_are_accepted() {
    let low = RequestRate::parse(1, 0).unwrap();
    assert_eq!((low.per_second(), low.burst()), (1, 0));

    let high = RequestRate::parse(MAXIMUM_PER_SECOND, MAXIMUM_BURST).unwrap();
    assert_eq!(
        (high.per_second(), high.burst()),
        (MAXIMUM_PER_SECOND, MAXIMUM_BURST)
    );
}

#[test]
fn a_rate_of_zero_is_refused_because_it_is_what_an_absent_field_decodes_to() {
    assert_eq!(RequestRate::parse(0, 0), Err(RequestRateError::Zero));
    assert_eq!(RequestRate::parse(0, 5), Err(RequestRateError::Zero));
}

#[test]
fn a_rate_above_the_maximum_is_refused() {
    assert_eq!(
        RequestRate::parse(MAXIMUM_PER_SECOND + 1, 0),
        Err(RequestRateError::RateTooHigh {
            value: MAXIMUM_PER_SECOND + 1,
            maximum: MAXIMUM_PER_SECOND,
        })
    );
}

#[test]
fn a_burst_above_the_maximum_is_refused() {
    assert_eq!(
        RequestRate::parse(10, MAXIMUM_BURST + 1),
        Err(RequestRateError::BurstTooHigh {
            value: MAXIMUM_BURST + 1,
            maximum: MAXIMUM_BURST,
        })
    );
}
