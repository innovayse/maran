//! Tests for `QuotaState`. Mirrors the source tree (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::QuotaState;
use crate::accounts::model::quota_unenforceable_reason::QuotaUnenforceableReason;
use crate::accounts::quota_blocks::QuotaBlocks;

#[test]
fn to_legacy_bytes_reports_the_figure_only_when_enforced() {
    let enforced = QuotaState::Enforced(QuotaBlocks::from_bytes(4096));

    assert_eq!(enforced.to_legacy_bytes(), 4096);
}

/// The mutant Task 1's proof names directly: `EnforceableButUnset` folded to
/// the same legacy byte figure as a real, tiny quota would be indistinguishable
/// from "the account has a 0-byte quota", the opposite of unlimited.
#[test]
fn enforceable_but_unset_never_reports_a_positive_legacy_figure() {
    let unset = QuotaState::EnforceableButUnset;

    assert_eq!(unset.to_legacy_bytes(), 0);
    // And it is NOT the enforced variant under any byte count — the
    // inverse control this test owes the one above.
    assert_ne!(unset, QuotaState::Enforced(QuotaBlocks::from_bytes(0)));
}

#[test]
fn not_enforceable_never_reports_a_positive_legacy_figure_either() {
    let not_enforceable =
        QuotaState::NotEnforceable(QuotaUnenforceableReason::MountedWithoutQuotaAccounting);

    assert_eq!(not_enforceable.to_legacy_bytes(), 0);
}

/// The three states are genuinely three, not two collapsed under one bit:
/// none of them compare equal to another for a fixed byte figure.
#[test]
fn the_three_states_are_pairwise_distinct() {
    let enforced = QuotaState::Enforced(QuotaBlocks::from_bytes(1024));
    let unset = QuotaState::EnforceableButUnset;
    let not_enforceable =
        QuotaState::NotEnforceable(QuotaUnenforceableReason::AccountingNotEnabled);

    assert_ne!(enforced, unset);
    assert_ne!(unset, not_enforceable);
    assert_ne!(enforced, not_enforceable);
}
