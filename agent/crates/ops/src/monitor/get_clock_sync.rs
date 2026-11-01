//! GetClockSync: is the host's clock kept in step with a time source?

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::monitor::model::clock_sync_state::ClockSyncState;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// The property naming whether a synchronisation service is enabled.
const SYNC_SERVICE_PROPERTY: &str = "NTP";

/// The property naming whether the kernel reports its clock synchronised.
const SYNCHRONIZED_PROPERTY: &str = "NTPSynchronized";

/// Asks `timedatectl show` whether the kernel clock is synchronised and
/// whether a synchronisation service is enabled (issue #150).
///
/// # Why this exists
///
/// Two-factor codes, token expiry, certificate validity and the licence lease
/// all compare against this host's clock, and each fails in its own confusing
/// way when the clock is wrong. Nothing on a running server checked it before
/// this operation existed.
///
/// # What it reads, and how
///
/// `timedatectl show --property=NTP --property=NTPSynchronized`, which prints
/// one `Key=value` line per property. `NTPSynchronized` is the kernel's own
/// flag (see [`ClockSyncState`] for what it does and does not prove), and
/// `NTP` says whether a synchronisation unit timedated knows of — timesyncd on
/// the Debian family, chronyd on the RHEL family — is enabled. The program is
/// the same on both families, so the operation does not branch on one.
///
/// # Errors
///
/// Returns [`MonitorError::ServiceManagerUnavailable`] when `timedatectl`
/// cannot be started, [`MonitorError::Bound`] when it was killed at its
/// deadline, and [`MonitorError::ClockStateUnavailable`] when it exited
/// non-zero or printed something other than both properties as `yes` or `no`.
/// None of these means "not synchronised": each means the agent could not
/// find out, which the caller must not fold into a finding.
pub fn get_clock_sync(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<ClockSyncState, MonitorError> {
    let outcome = host.run(
        &distro.timedatectl_binary(),
        &[
            "show",
            "--property=NTP",
            "--property=NTPSynchronized",
        ],
        CallClass::Query,
    )?;
    if outcome.status != 0 {
        return Err(MonitorError::ClockStateUnavailable);
    }

    let synchronized = property(&outcome.stdout, SYNCHRONIZED_PROPERTY)?;
    let sync_service_enabled = property(&outcome.stdout, SYNC_SERVICE_PROPERTY)?;

    Ok(if synchronized {
        ClockSyncState::Synchronized
    } else if sync_service_enabled {
        ClockSyncState::NotSynchronized
    } else {
        ClockSyncState::SyncServiceDisabled
    })
}

/// Reads one boolean property out of `timedatectl show`'s `Key=value` lines.
///
/// The last line naming the key wins, the way every key-value reading in this
/// agent resolves a repeat (rules/rust.md "Reading a live config back").
///
/// # Errors
///
/// Returns [`MonitorError::ClockStateUnavailable`] when the key is absent or
/// its value is neither `yes` nor `no`: an unreadable answer is not a `no`.
fn property(stdout: &str, key: &str) -> Result<bool, MonitorError> {
    let value = stdout.lines().rev().find_map(|line| {
        line.trim()
            .split_once('=')
            .filter(|(name, _)| *name == key)
            .map(|(_, value)| value)
    });

    match value {
        Some("yes") => Ok(true),
        Some("no") => Ok(false),
        _ => Err(MonitorError::ClockStateUnavailable),
    }
}

#[cfg(test)]
#[path = "../tests/monitor/get_clock_sync_tests.rs"]
mod tests;
