//! Tests for the `tasks_max` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, TasksMax};
use crate::validation::system::tasks_max_error::TasksMaxError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(TasksMax::parse(1).unwrap().value(), 1);
    assert_eq!(TasksMax::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(TasksMax::parse(0), Err(TasksMaxError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        TasksMax::parse(MAXIMUM + 1),
        Err(TasksMaxError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}

#[test]
fn the_property_is_the_key_and_the_digits_only() {
    assert_eq!(TasksMax::parse(42).unwrap().as_property(), "TasksMax=42");
}
