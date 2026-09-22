//! Tests for `to_wire_quota_state`. Mirrors the source tree
//! (rules/testing.md).
//!
//! [`maran_ops::accounts::model::quota_state::QuotaBlocks`] is not exported
//! outside the `ops` crate at all — `Enforced`'s inner value cannot be
//! constructed from here, so that branch is proven where it CAN be built:
//! `ops`'s own `quota_state_tests.rs` and `account_operations_tests.rs`
//! (`usage_reports_the_measured_tree_and_the_hard_limit_when_enforceable`).
//! This file covers the two branches that are reachable from outside the
//! crate, which is exactly the boundary `to_wire_quota_state` itself sits on.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::{QuotaState, QuotaUnenforceableReason};

use super::to_wire_quota_state;
use crate::proto::{QuotaState as WireQuotaState, QuotaUnenforceableReason as WireReason};

#[test]
fn enforceable_but_unset_carries_no_reason_and_a_zero_legacy_figure() {
    let (state, bytes, reason) = to_wire_quota_state(QuotaState::EnforceableButUnset);

    assert_eq!(state, WireQuotaState::EnforceableButUnset);
    assert_eq!(bytes, 0);
    assert_eq!(reason, None);
}

/// The mutant this proof names: reporting `EnforceableButUnset` as
/// `NotEnforceable` (or the reverse) sends the panel the exact wrong
/// commercial fact — "cannot enforce" when a limit could be set, or "no
/// limit configured" when the filesystem cannot back one at all.
#[test]
fn not_enforceable_carries_its_specific_reason() {
    let (state, bytes, reason) = to_wire_quota_state(QuotaState::NotEnforceable(
        QuotaUnenforceableReason::AccountingNotEnabled,
    ));

    assert_eq!(state, WireQuotaState::NotEnforceable);
    assert_eq!(bytes, 0);
    assert_eq!(reason, Some(WireReason::AccountingNotEnabled));
}

#[test]
fn the_other_reason_is_not_swapped_for_this_one() {
    let (_, _, reason) = to_wire_quota_state(QuotaState::NotEnforceable(
        QuotaUnenforceableReason::MountedWithoutQuotaAccounting,
    ));

    assert_eq!(reason, Some(WireReason::MountedWithoutQuotaAccounting));
}
