//! The bounds a walk keeps, and the ceilings above whatever a request states.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TreeLimits;

#[test]
fn a_stated_allowance_below_the_ceiling_is_kept() {
    let limits = TreeLimits::stated(100, 2000);

    assert_eq!((limits.max_entries, limits.max_bytes), (100, 2000));
}

#[test]
fn a_stated_allowance_above_the_ceiling_is_clamped_to_it() {
    let limits = TreeLimits::stated(u64::MAX, u64::MAX);

    assert_eq!(limits.max_entries, TreeLimits::CEILING_ENTRIES);
    assert_eq!(limits.max_bytes, TreeLimits::CEILING_BYTES);
}

#[test]
fn the_ceilings_alone_state_no_byte_allowance() {
    let limits = TreeLimits::ceilings();

    assert_eq!(limits.max_entries, TreeLimits::CEILING_ENTRIES);
    assert_eq!(limits.max_bytes, u64::MAX);
}

#[test]
fn the_depth_ceiling_is_one_hundred_and_twenty_eight() {
    assert_eq!(TreeLimits::MAXIMUM_DEPTH, 128);
}
