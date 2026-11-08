//! Tests for the `static_cache_days` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_DAYS, StaticCacheDays, StaticCacheDaysError};

#[test]
fn the_ends_of_the_range_are_accepted() {
    assert_eq!(StaticCacheDays::parse(1).unwrap().days(), 1);
    assert_eq!(
        StaticCacheDays::parse(MAXIMUM_DAYS).unwrap().days(),
        MAXIMUM_DAYS
    );
}

#[test]
fn zero_is_refused_because_no_cache_rule_is_the_absence_of_the_type() {
    assert_eq!(StaticCacheDays::parse(0), Err(StaticCacheDaysError::Zero));
}

#[test]
fn a_lifetime_above_a_year_is_refused() {
    assert_eq!(
        StaticCacheDays::parse(MAXIMUM_DAYS + 1),
        Err(StaticCacheDaysError::TooLarge {
            value: MAXIMUM_DAYS + 1,
            maximum: MAXIMUM_DAYS,
        })
    );
}
