//! What the panel can honestly say about one account's disk quota.

use super::super::quota_blocks::QuotaBlocks;
use super::quota_state_kind::QuotaStateKind;
use super::quota_unenforceable_reason::QuotaUnenforceableReason;

/// One account's disk quota, as three states rather than a number that might mean nothing.
///
/// # Why three and not an `Option`
///
/// This type replaced an `Option<QuotaBlocks>` whose `None` meant two different things: this
/// account has no limit set, and **this filesystem cannot enforce a limit at all** because it was
/// never mounted with quota accounting. Those are not the same fact and must not share an answer:
/// a disk limit is something a customer PAID for, so a host that cannot enforce one while the
/// panel displays the sold figure is asserting something false, and nothing anywhere could
/// contradict it. Collapsing the two is how that went unnoticed.
///
/// # What this type does not decide
///
/// It carries what was observed; it does not display anything. The figure the panel SHOWS a
/// customer is still the plan's own stored megabytes and deliberately not derived from the
/// filesystem — asking the host to re-derive it would create a second answer that can disagree
/// with the first. Enforceability is a fact recorded BESIDE the sold figure, never a replacement
/// for it.
// `QuotaBlocks` is `pub(super)` and stays that way: a block count is this module's own unit, and a
// caller outside the crate has no business converting to or from it — that is precisely why
// `QuotaStateKind` exists and why `to_legacy_bytes` is the only way out. Naming it in a `pub`
// variant therefore trips `private_interfaces`, which is the correct lint reporting a deliberate
// choice rather than a mistake: the variant is constructible only inside `accounts`, and every
// outside reader goes through `kind()`. Widening `QuotaBlocks` to `pub` to silence this would make
// the unit part of the public surface, which is the opposite of what was wanted.
#[allow(private_interfaces)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaState {
    /// The filesystem enforces quotas and this account has one, of this many blocks.
    Enforced(QuotaBlocks),
    /// The filesystem enforces quotas and this account has no limit set — an honest "unlimited".
    EnforceableButUnset,
    /// The filesystem cannot enforce a limit at all, for the reason carried here. Any limit the
    /// panel holds for this account is, on this host, a number nobody applies.
    NotEnforceable(QuotaUnenforceableReason),
}

impl QuotaState {
    /// This state's shape, for a caller that must distinguish the three without naming
    /// `QuotaBlocks`.
    ///
    /// Callers outside this crate branch on this rather than on the value itself, so that adding a
    /// fourth state later is an additive change for them (`#[non_exhaustive]`) instead of a broken
    /// build.
    /// @returns The matching [`QuotaStateKind`].
    #[must_use]
    pub fn kind(&self) -> QuotaStateKind {
        match self {
            Self::Enforced(_) => QuotaStateKind::Enforced,
            Self::EnforceableButUnset => QuotaStateKind::EnforceableButUnset,
            Self::NotEnforceable(reason) => QuotaStateKind::NotEnforceable(*reason),
        }
    }

    /// The byte figure the pre-existing wire field carries, which is `0` for both non-enforced
    /// states.
    ///
    /// **A `0` here means "unproduced", never "no quota"** — the same meaning the deprecated
    /// `quota_bytes` field has always had on the wire. That ambiguity is exactly why `kind` exists
    /// and why a reader must consult it rather than treating a zero as a limit of zero.
    /// @returns The hard limit in bytes when enforced, and `0` otherwise.
    #[must_use]
    pub fn to_legacy_bytes(&self) -> u64 {
        match self {
            Self::Enforced(blocks) => blocks.to_bytes(),
            Self::EnforceableButUnset | Self::NotEnforceable(_) => 0,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/accounts/quota_state_tests.rs"]
mod tests;
