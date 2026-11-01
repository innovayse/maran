//! Tests for the `cpu_quota_percent` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CpuQuotaPercent, MAXIMUM};
use crate::validation::system::cpu_quota_percent_error::CpuQuotaPercentError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(CpuQuotaPercent::parse(1).unwrap().value(), 1);
    assert_eq!(CpuQuotaPercent::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(CpuQuotaPercent::parse(0), Err(CpuQuotaPercentError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        CpuQuotaPercent::parse(MAXIMUM + 1),
        Err(CpuQuotaPercentError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}

#[test]
fn the_property_is_the_key_and_the_digits_only() {
    assert_eq!(
        CpuQuotaPercent::parse(42).unwrap().as_property(),
        "CPUQuota=42%"
    );
}
