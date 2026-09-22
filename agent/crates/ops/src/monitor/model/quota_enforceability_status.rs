//! What the panel found when it checked whether the filesystem holding
//! hosting accounts' homes can enforce a disk quota right now.

pub use crate::accounts::QuotaUnenforceableReason;

/// Whether the filesystem holding
/// [`maran_agent_core::agent_paths::AgentPaths::ACCOUNT_HOME_ROOT`] can
/// currently enforce a per-user disk quota.
///
/// Deliberately the two enforceability-only states of
/// [`crate::accounts::QuotaState`] and not that type itself: this area has no
/// account to report a byte figure for, only a host-wide fact about the
/// filesystem — the account-level figure stays exactly where Section 6 of
/// `docs/superpowers/plans/2026-09-19-maran-quota-enforceability.md` puts it,
/// in the accounts area's own `usage()`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaEnforceabilityStatus {
    /// The filesystem can enforce a quota right now.
    Enforceable,
    /// It cannot, for the stated reason.
    NotEnforceable(QuotaUnenforceableReason),
}
