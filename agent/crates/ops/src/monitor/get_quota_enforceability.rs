//! GetQuotaEnforceability: can the filesystem holding hosting accounts' homes
//! currently enforce a per-user disk quota?

use maran_distro::DistroAdapter;

use maran_agent_core::agent_paths::AgentPaths;

use crate::accounts::classify_quota_enforceability;
use crate::monitor::model::quota_enforceability_status::QuotaEnforceabilityStatus;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Reads `/proc/mounts` and runs `quotaon -p` against
/// [`AgentPaths::ACCOUNT_HOME_ROOT`] to answer whether it can enforce a
/// per-user disk quota.
///
/// # Why this exists
///
/// A remount can turn an enforceable filesystem unenforceable — or the other
/// way round — without touching a single account, so the one-shot check the
/// installer's preflight runs (`installer/lib/10-preflight.sh`,
/// `check_quota_capable`) is not enough; nothing on a running server
/// re-checked this before this operation existed. It exists for the same
/// reason [`crate::monitor::get_sftp_jail_status`] does: an installer-time
/// fact can go stale, and only a continuous reading catches that.
///
/// # What this checks, and how
///
/// Exactly the two observations
/// [`crate::accounts`]'s own `usage()` decides enforceability with, through
/// the one shared classifier (`classify_quota_enforceability`) so the rule
/// lives in a single place: `/proc/mounts`'s OPTIONS field (necessary, not
/// sufficient — a mount option can outlive a `quotaoff` run by hand) and
/// `quotaon -p`'s own live answer (decisive — it is what `setquota` itself
/// consults). See `crate::accounts::quota_enforceability`'s module doc for
/// the full argument, repeated there rather than here because that is where
/// the two observations are actually taken apart.
///
/// # What this cannot see
///
/// A network filesystem mounted for `/home` — NFS, most concretely — can
/// report a LOCAL `quotaon -p` state disconnected from the SERVER's own
/// enforcement (`rpc.rquotad`), which this agent has no rpc path to. Named
/// here again because this is the rpc an operator's dashboard reads, and the
/// gap must be visible from there too, not only in the source comment nobody
/// operating a panel will open.
///
/// # Errors
///
/// Returns [`MonitorError::MountsUnavailable`] when `/proc/mounts` cannot be
/// read, and [`MonitorError::ServiceManagerUnavailable`] when `quotaon`
/// cannot be started. Neither means "not enforceable" — both mean this agent
/// could not even ask the question, which the caller must not fold into a
/// confident negative finding.
pub fn get_quota_enforceability(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<QuotaEnforceabilityStatus, MonitorError> {
    let mounts = host.read_mounts()?;
    let outcome = host.run(
        distro.quotaon_binary(),
        &["-p", AgentPaths::ACCOUNT_HOME_ROOT],
    )?;

    Ok(
        match classify_quota_enforceability(&mounts, &outcome.stdout, AgentPaths::ACCOUNT_HOME_ROOT)
        {
            Ok(()) => QuotaEnforceabilityStatus::Enforceable,
            Err(reason) => QuotaEnforceabilityStatus::NotEnforceable(reason),
        },
    )
}

#[cfg(test)]
#[path = "../tests/monitor/get_quota_enforceability_tests.rs"]
mod tests;
