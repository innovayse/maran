//! Tests for the allowances of a copy.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TransferBounds;
use crate::validation::fs::transfer_bounds_error::TransferBoundsError;

#[test]
fn stated_caps_are_carried() {
    let bounds = TransferBounds::parse(10, 2048).unwrap();

    assert_eq!(bounds.max_entries(), 10);
    assert_eq!(bounds.max_bytes(), 2048);
}

#[test]
fn the_ceilings_themselves_are_accepted() {
    assert!(
        TransferBounds::parse(
            TransferBounds::CEILING_ENTRIES,
            TransferBounds::CEILING_BYTES
        )
        .is_ok()
    );
}

#[test]
fn a_zero_cap_is_not_stated() {
    for (entries, bytes) in [(0, 1), (1, 0), (0, 0)] {
        assert_eq!(
            TransferBounds::parse(entries, bytes).unwrap_err(),
            TransferBoundsError::NotStated
        );
    }
}

#[test]
fn a_cap_above_its_ceiling_is_refused() {
    assert_eq!(
        TransferBounds::parse(TransferBounds::CEILING_ENTRIES + 1, 1).unwrap_err(),
        TransferBoundsError::AboveCeiling
    );
    assert_eq!(
        TransferBounds::parse(1, TransferBounds::CEILING_BYTES + 1).unwrap_err(),
        TransferBoundsError::AboveCeiling
    );
}
