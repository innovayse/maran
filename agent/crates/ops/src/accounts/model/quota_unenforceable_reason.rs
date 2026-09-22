//! What the panel can honestly say about one account's disk quota.

/// The reason a filesystem cannot hold an enforceable disk quota, as far as
/// this agent can tell from where it is standing.
///
/// `#[non_exhaustive]`: a future observation (an NFS-aware check, a third quota
/// mechanism) is added as a new variant, and every `match` in this crate fails
/// to compile until it is handled — the same protection a named catch-all
/// would give, without inventing a variant that means nothing
/// (`agent/crates/ops/src/db/model/grant_repair_refusal.rs`'s idiom).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaUnenforceableReason {
    /// The filesystem holding the account's home is not mounted with a quota
    /// accounting option (`usrquota`/`uquota`/`usrjquota`), so no per-user
    /// limit can exist on it no matter what `setquota` is asked to do.
    MountedWithoutQuotaAccounting,

    /// The filesystem is mounted with a quota accounting option, but
    /// `quotaon -p` reports user quota accounting is OFF for it — mounted
    /// correctly but never turned on (or turned off since).
    AccountingNotEnabled,
}
