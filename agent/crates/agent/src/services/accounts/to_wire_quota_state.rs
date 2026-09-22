//! The one mapping from the agent's own [`QuotaState`] onto the wire enums.

use maran_ops::accounts::{QuotaState, QuotaStateKind, QuotaUnenforceableReason};

use crate::proto::{QuotaState as WireQuotaState, QuotaUnenforceableReason as WireReason};

/// Converts a reading into the wire enum carried on `GetAccountUsageOk`,
/// plus the legacy `quota_bytes` mirror and the reason, when there is one.
///
/// One state maps to one wire value in exactly one place (rules/rust.md
/// "Service anatomy"), and `Unspecified` is never produced here for the same
/// reason [`super::to_login_password_state::to_login_password_state`] never
/// produces it: an agent that can classify enforceability at all always has
/// one of the three real answers, and `Unspecified` on the wire means "the
/// agent predates this field."
///
/// `QuotaState` is `#[non_exhaustive]` too, crossing the same crate boundary
/// — a wildcard arm covers a future variant with the same "unspecified" wire
/// fallback rather than a broken build.
#[must_use]
pub fn to_wire_quota_state(state: QuotaState) -> (WireQuotaState, u64, Option<WireReason>) {
    // The byte figure comes from `QuotaState::to_legacy_bytes`, and the
    // shape from `QuotaState::kind`, never from matching `state` directly:
    // `Enforced`'s inner type (`ops::accounts::QuotaBlocks`) is deliberately
    // not nameable outside the `ops` crate at all, which blocks even a
    // wildcard pattern on `state` from this crate — `kind()` is the
    // sanctioned way across that boundary.
    let bytes = state.to_legacy_bytes();

    match state.kind() {
        QuotaStateKind::Enforced => (WireQuotaState::Enforced, bytes, None),
        QuotaStateKind::EnforceableButUnset => (WireQuotaState::EnforceableButUnset, bytes, None),
        QuotaStateKind::NotEnforceable(reason) => (
            WireQuotaState::NotEnforceable,
            bytes,
            Some(to_wire_reason(reason)),
        ),
        _ => (WireQuotaState::Unspecified, bytes, None),
    }
}

/// Converts the specific unenforceable reason onto its wire mirror.
///
/// [`QuotaUnenforceableReason`] is `#[non_exhaustive]`, so — crossing the
/// crate boundary from `ops` — this match must carry a wildcard arm; a
/// reason this file's author did not anticipate falls back to
/// [`WireReason::Unspecified`] rather than failing to compile, which is the
/// deliberate cost of the protection: a future `ops::accounts` variant is
/// caught here as a silent "unspecified" on the wire rather than a broken
/// build, and this arm is the one place a reviewer should look when a new
/// variant is added upstream.
#[must_use]
fn to_wire_reason(reason: QuotaUnenforceableReason) -> WireReason {
    match reason {
        QuotaUnenforceableReason::MountedWithoutQuotaAccounting => {
            WireReason::MountedWithoutQuotaAccounting
        }
        QuotaUnenforceableReason::AccountingNotEnabled => WireReason::AccountingNotEnabled,
        _ => WireReason::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/to_wire_quota_state_tests.rs"]
mod tests;
