//! What the panel can honestly say about one account's disk quota.

use super::quota_unenforceable_reason::QuotaUnenforceableReason;

/// [`super::quota_state::QuotaState`]'s shape alone, for a caller outside this crate that needs
/// to distinguish the three states but has no use for — and cannot even
/// name — the byte figure's private inner type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaStateKind {
    /// Mirrors [`super::quota_state::QuotaState::Enforced`]; read [`super::quota_state::QuotaState::to_legacy_bytes`]
    /// on the original value for the figure.
    Enforced,
    /// Mirrors [`super::quota_state::QuotaState::EnforceableButUnset`].
    EnforceableButUnset,
    /// Mirrors [`super::quota_state::QuotaState::NotEnforceable`].
    NotEnforceable(QuotaUnenforceableReason),
}
