//! Tests for the `inode_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::InodeLimit;
use crate::validation::system::inode_limit_error::InodeLimitError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(InodeLimit::parse(1).unwrap().value(), 1);
    assert_eq!(InodeLimit::parse(u32::MAX).unwrap().value(), u32::MAX);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(InodeLimit::parse(0), Err(InodeLimitError::Zero));
}

#[test]
fn the_argument_is_the_digits_only() {
    assert_eq!(InodeLimit::parse(250_000).unwrap().as_argument(), "250000");
}

#[test]
fn the_unlimited_argument_is_the_figure_the_type_refuses() {
    // The two facts the constant's comment puts side by side: the tool reads
    // `0` as unlimited, and the type refuses `0` for exactly that reason.
    assert_eq!(InodeLimit::UNLIMITED_ARGUMENT, "0");
    assert!(InodeLimit::parse(0).is_err());
}
