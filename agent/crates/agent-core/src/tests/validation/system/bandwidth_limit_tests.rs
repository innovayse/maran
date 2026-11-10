//! Tests for the `bandwidth_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::BandwidthLimit;
use crate::validation::system::bandwidth_limit_error::BandwidthLimitError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(BandwidthLimit::parse(1).unwrap().kibibytes_per_second(), 1);
    assert_eq!(
        BandwidthLimit::parse(1_048_576)
            .unwrap()
            .kibibytes_per_second(),
        1_048_576
    );
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(BandwidthLimit::parse(0), Err(BandwidthLimitError::Zero));
}

#[test]
fn a_figure_above_the_ceiling_is_refused() {
    assert_eq!(
        BandwidthLimit::parse(1_048_577),
        Err(BandwidthLimitError::TooHigh { maximum: 1_048_576 })
    );
}

#[test]
fn the_argument_is_the_option_and_the_digits_only() {
    assert_eq!(
        BandwidthLimit::parse(2048).unwrap().as_argument(),
        "--bwlimit=2048"
    );
}
