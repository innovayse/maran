//! Tests for [`Deadline`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::utils::deadline::Deadline;

/// A zero span has passed at once.
#[test]
fn a_zero_deadline_has_passed() {
    assert!(Deadline::after(Duration::ZERO).has_passed());
}

/// A far span has not passed (inverse control of the zero case).
#[test]
fn a_distant_deadline_has_not_passed() {
    assert!(!Deadline::after(Duration::from_secs(3_600)).has_passed());
}

/// A span too large for the clock never passes and never panics.
#[test]
fn a_span_too_large_for_the_clock_never_passes_and_does_not_panic() {
    assert!(!Deadline::after(Duration::MAX).has_passed());
}
