//! Tests for the `open_files_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, OpenFilesLimit};
use crate::validation::system::open_files_limit_error::OpenFilesLimitError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(OpenFilesLimit::parse(1).unwrap().value(), 1);
    assert_eq!(OpenFilesLimit::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn the_maximum_is_two_to_the_twentieth() {
    assert_eq!(MAXIMUM, 1_048_576);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(OpenFilesLimit::parse(0), Err(OpenFilesLimitError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        OpenFilesLimit::parse(MAXIMUM + 1),
        Err(OpenFilesLimitError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}
