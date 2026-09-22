//! Classifies, from two raw text observations, whether a filesystem can
//! enforce a disk quota — pure logic shared by [`super::AccountOperations`]
//! (which needs the answer before it trusts a byte count, Section 4 of
//! `docs/superpowers/plans/2026-09-19-maran-quota-enforceability.md`) and the
//! Monitoring module's continuous drift check (`ops::monitor`), so the
//! mount-option and `quotaon -p` reading rule exists in exactly one place.
//!
//! # What this checks, and how
//!
//! Two observations, deliberately not one (see the plan's Section 3 for the
//! argument in full):
//!
//! 1. `/proc/mounts`'s OPTIONS field for the filesystem holding
//!    [`maran_agent_core::agent_paths::AgentPaths::ACCOUNT_HOME_ROOT`] — a
//!    MOUNT-TIME signal. Necessary but not sufficient: a filesystem can carry
//!    `usrquota` in its mount options for months after `quotaon` was run once
//!    and then turned off, or after a `quotacheck` was interrupted.
//! 2. `quotaon -p <mountpoint>`'s own output — the kernel's LIVE state, and
//!    the decisive one. It is what `setquota` itself consults, so it is the
//!    signal closest to "will a limit actually bind."
//!
//! Observation 1 is kept only to give the more specific
//! [`QuotaUnenforceableReason::MountedWithoutQuotaAccounting`] reason when it
//! is the mount itself that never asked for quota accounting, rather than the
//! generic "turn it on" — observation 2 alone cannot tell those two cases
//! apart, because a filesystem never mounted with the option also fails
//! `quotaon -p`.
//!
//! # What this cannot see
//!
//! A network filesystem mounted for `/home` — NFS, most concretely — can
//! report a LOCAL `quotaon -p` state entirely disconnected from enforcement,
//! because NFS quota accounting (where it exists at all, via `rpc.rquotad`)
//! is enforced on the SERVER the export lives on, not on the client this
//! agent runs on. This agent has no rpc path to a remote NFS server's quota
//! daemon, and the product's supported layout does not use NFS for `/home` in
//! v1. [`QuotaUnenforceableReason`] is `#[non_exhaustive]` precisely so a
//! future NFS-aware variant can be added without a breaking change; this
//! module does not pretend to cover that case today.

use super::model::quota_unenforceable_reason::QuotaUnenforceableReason;

/// Whether `mounts_text` (verbatim `/proc/mounts` content) shows `mountpoint`
/// mounted with a quota-accounting option (`usrquota`, `uquota`, or
/// `usrjquota` — one covers ext-family filesystems, the other two XFS).
#[must_use]
fn mounted_with_quota_accounting(mounts_text: &str, mountpoint: &str) -> bool {
    mounts_text.lines().any(|line| {
        let mut fields = line.split_whitespace();
        let _device = fields.next();
        let Some(mount_point) = fields.next() else {
            return false;
        };
        if mount_point != mountpoint {
            return false;
        }
        let _fstype = fields.next();
        let Some(options) = fields.next() else {
            return false;
        };
        options
            .split(',')
            .any(|option| matches!(option, "usrquota" | "uquota" | "usrjquota"))
    })
}

/// Whether `quotaon_p_output` (the verbatim stdout of `quotaon -p
/// <mountpoint>`) reports user quota accounting as ON.
///
/// `quotaon -p` prints a line per filesystem asked about, of the shape
/// `Quota for users are enabled on mountpoint <path> ...` when on, and
/// something else — commonly `Quota for users are not enabled ...` or an
/// error to standard error with nothing useful on standard out — otherwise.
/// Parsed by substring rather than a fixed column count because the exact
/// wording differs slightly between `quota-tools` releases; "enabled" is the
/// one word every release's positive answer is documented to contain.
#[must_use]
fn quotaon_reports_enabled(quotaon_p_output: &str) -> bool {
    quotaon_p_output
        .lines()
        .any(|line| line.contains("are enabled") || line.contains("is enabled"))
}

/// Classifies enforceability from the two raw observations.
///
/// `Ok(())` means the filesystem can enforce a quota right now; `Err` carries
/// the specific reason it cannot, per the rule above: a mount that never
/// asked for quota accounting gets
/// [`QuotaUnenforceableReason::MountedWithoutQuotaAccounting`], and a mount
/// that asked but where `quotaon -p` reports it off gets
/// [`QuotaUnenforceableReason::AccountingNotEnabled`].
pub(crate) fn classify(
    mounts_text: &str,
    quotaon_p_output: &str,
    mountpoint: &str,
) -> Result<(), QuotaUnenforceableReason> {
    if quotaon_reports_enabled(quotaon_p_output) {
        return Ok(());
    }

    if mounted_with_quota_accounting(mounts_text, mountpoint) {
        Err(QuotaUnenforceableReason::AccountingNotEnabled)
    } else {
        Err(QuotaUnenforceableReason::MountedWithoutQuotaAccounting)
    }
}

#[cfg(test)]
#[path = "../tests/accounts/quota_enforceability_tests.rs"]
mod tests;
