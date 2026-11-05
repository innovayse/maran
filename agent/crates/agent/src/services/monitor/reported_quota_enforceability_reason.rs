//! What a quota-unenforceable reason looks like on the wire.

use crate::proto::QuotaEnforceabilityReason;

/// Converts the agent's own reason onto its wire mirror.
///
/// `QuotaUnenforceableReason` is `#[non_exhaustive]`: a wildcard covers a
/// future variant with the same "unspecified" wire fallback the accounts
/// service's own `to_wire_quota_state` uses, rather than a broken build.
#[must_use]
pub fn reported_quota_enforceability_reason(
    reason: maran_ops::accounts::QuotaUnenforceableReason,
) -> QuotaEnforceabilityReason {
    match reason {
        maran_ops::accounts::QuotaUnenforceableReason::MountedWithoutQuotaAccounting => {
            QuotaEnforceabilityReason::MountedWithoutQuotaAccounting
        }
        maran_ops::accounts::QuotaUnenforceableReason::AccountingNotEnabled => {
            QuotaEnforceabilityReason::AccountingNotEnabled
        }
        _ => QuotaEnforceabilityReason::Unspecified,
    }
}
