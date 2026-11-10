//! Tests for [`VhostBudget`]: the files and bytes of configuration a read may hold.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::vhost_budget::VhostBudget;

/// Files are given out until the bound and then refused.
#[test]
fn files_are_given_out_until_the_bound() {
    let mut budget = VhostBudget::new();

    for _ in 0..InventoryLimits::MAX_VHOST_FILES {
        assert!(budget.take_file());
    }
    assert!(!budget.take_file());
}

/// Bytes are given out until the bound; a request that does not fit spends
/// nothing, and one that fits exactly is the inverse control.
#[test]
fn bytes_are_given_out_until_the_bound_and_a_refusal_spends_nothing() {
    let mut budget = VhostBudget::new();

    assert!(budget.take_bytes(InventoryLimits::MAX_VHOST_BYTES - 10));
    assert!(!budget.take_bytes(11));
    assert!(budget.take_bytes(10));
    assert!(!budget.take_bytes(1));
}
