//! Tests for the caps an archive creation states.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::CreateBounds;
use crate::validation::fs::archive_bounds_error::ArchiveBoundsError;
use crate::validation::fs::extract_bounds::{MAXIMUM_ENTRIES, MAXIMUM_EXPANDED_BYTES};

#[test]
fn caps_inside_the_ceilings_are_kept_as_stated() {
    let bounds = CreateBounds::parse(500, 4096).unwrap();

    assert_eq!(bounds.max_entries(), 500);
    assert_eq!(bounds.max_source_bytes(), 4096);
}

#[test]
fn the_ceilings_themselves_are_accepted() {
    assert!(CreateBounds::parse(MAXIMUM_ENTRIES, MAXIMUM_EXPANDED_BYTES).is_ok());
}

#[test]
fn a_zero_cap_is_refused_not_read_as_unlimited() {
    assert_eq!(
        CreateBounds::parse(0, 1).unwrap_err(),
        ArchiveBoundsError::NoEntryCap
    );
    assert_eq!(
        CreateBounds::parse(1, 0).unwrap_err(),
        ArchiveBoundsError::NoByteCap
    );
}

#[test]
fn a_cap_above_the_ceiling_is_refused_not_clamped() {
    assert_eq!(
        CreateBounds::parse(MAXIMUM_ENTRIES + 1, 1).unwrap_err(),
        ArchiveBoundsError::EntryCapTooHigh
    );
    assert_eq!(
        CreateBounds::parse(1, MAXIMUM_EXPANDED_BYTES + 1).unwrap_err(),
        ArchiveBoundsError::ByteCapTooHigh
    );
}
