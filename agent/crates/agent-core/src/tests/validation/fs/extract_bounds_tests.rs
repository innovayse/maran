//! Tests for the caps an extraction states.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{ExtractBounds, MAXIMUM_ENTRIES, MAXIMUM_EXPANDED_BYTES, MAXIMUM_RATIO};
use crate::validation::fs::archive_bounds_error::ArchiveBoundsError;

#[test]
fn caps_inside_the_ceilings_are_kept_as_stated() {
    let bounds = ExtractBounds::parse(100_000, 2 * 1024 * 1024 * 1024, 100).unwrap();

    assert_eq!(bounds.max_entries(), 100_000);
    assert_eq!(bounds.max_expanded_bytes(), 2 * 1024 * 1024 * 1024);
    assert_eq!(bounds.max_ratio(), 100);
}

#[test]
fn the_ceilings_themselves_are_accepted() {
    assert!(ExtractBounds::parse(MAXIMUM_ENTRIES, MAXIMUM_EXPANDED_BYTES, MAXIMUM_RATIO).is_ok());
}

#[test]
fn a_zero_cap_is_refused_not_read_as_unlimited() {
    assert_eq!(
        ExtractBounds::parse(0, 1, 1).unwrap_err(),
        ArchiveBoundsError::NoEntryCap
    );
    assert_eq!(
        ExtractBounds::parse(1, 0, 1).unwrap_err(),
        ArchiveBoundsError::NoByteCap
    );
    assert_eq!(
        ExtractBounds::parse(1, 1, 0).unwrap_err(),
        ArchiveBoundsError::NoRatioCap
    );
}

#[test]
fn a_cap_above_the_ceiling_is_refused_not_clamped() {
    assert_eq!(
        ExtractBounds::parse(MAXIMUM_ENTRIES + 1, 1, 1).unwrap_err(),
        ArchiveBoundsError::EntryCapTooHigh
    );
    assert_eq!(
        ExtractBounds::parse(1, MAXIMUM_EXPANDED_BYTES + 1, 1).unwrap_err(),
        ArchiveBoundsError::ByteCapTooHigh
    );
    assert_eq!(
        ExtractBounds::parse(1, 1, MAXIMUM_RATIO + 1).unwrap_err(),
        ArchiveBoundsError::RatioCapTooHigh
    );
}
