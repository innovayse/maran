//! Tests for the entry-read budget.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SearchBudget;

#[test]
fn the_standard_budget_is_two_hundred_thousand_entries() {
    assert_eq!(SearchBudget::standard().remaining(), 200_000);
}

#[test]
fn spending_reduces_what_remains() {
    let mut budget = SearchBudget::of(10);

    budget.spend(4);

    assert_eq!(budget.remaining(), 6);
}

#[test]
fn spending_more_than_remains_stops_at_zero() {
    let mut budget = SearchBudget::of(3);

    budget.spend(5);

    assert_eq!(budget.remaining(), 0);
}
