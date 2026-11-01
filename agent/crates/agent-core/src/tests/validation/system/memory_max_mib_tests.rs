//! Tests for the `memory_max_mib` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, MemoryMaxMib};
use crate::validation::system::memory_max_mib_error::MemoryMaxMibError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(MemoryMaxMib::parse(1).unwrap().value(), 1);
    assert_eq!(MemoryMaxMib::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(MemoryMaxMib::parse(0), Err(MemoryMaxMibError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        MemoryMaxMib::parse(MAXIMUM + 1),
        Err(MemoryMaxMibError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}

#[test]
fn the_property_is_the_key_and_the_digits_only() {
    assert_eq!(
        MemoryMaxMib::parse(42).unwrap().as_property(),
        "MemoryMax=42M"
    );
}
