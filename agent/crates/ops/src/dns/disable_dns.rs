//! Turning DNS off, and keeping it off across a reboot.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::get_dns_status::get_dns_status;
use crate::dns::model::dns_state::DnsState;
use crate::dns::model::dns_unit::DnsUnit;

/// Stops the DNS daemon and takes it out of the boot sequence.
///
/// Idempotent: `systemctl disable --now` on a stopped, disabled unit exits zero
/// and does no work, which is what makes a repeated disable safe after a lost
/// response. Both halves are one command on purpose: stopping without disabling
/// leaves a unit that comes back at the next reboot, the operator's decision
/// silently undone by a power cut.
///
/// The configuration, the zone files and the daemon's zone list are left exactly
/// where they are. They are inert while the daemon is stopped, and keeping them
/// means a later enable converges without rewriting anything. The firewall is
/// not touched either: closing port 53 is its own administrator action.
///
/// # Errors
///
/// Returns [`DnsError::ServiceRefused`] when the service manager refuses to stop
/// or disable the unit, [`DnsError::SpawnFailed`] when it cannot be started at
/// all, [`DnsError::Bound`] when it outruns its deadline, and
/// [`DnsError::ConfigUnreadable`] when the live configuration exists and cannot
/// be read while the resulting state is observed.
pub fn disable_dns(host: &dyn DnsHost, distro: &dyn DistroAdapter) -> Result<DnsState, DnsError> {
    let outcome = host.run(
        &distro.service_manager(),
        &DnsUnit::DISABLE_NOW,
        CallClass::Reload,
    )?;
    if outcome.status != 0 {
        return Err(DnsError::ServiceRefused {
            unit: AgentPaths::DNS_UNIT.to_owned(),
        });
    }

    // Observed afterwards rather than assumed: this function's own success is not
    // evidence that the daemon stopped.
    get_dns_status(host, distro)
}

#[cfg(test)]
#[path = "../tests/dns/disable_dns_tests.rs"]
mod tests;
