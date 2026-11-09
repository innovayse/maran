//! Waiting, for a bounded time, for a freshly restarted DNS daemon to answer.

use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::rndc_arguments::rndc_arguments;

/// How long between two asks.
const STEP: Duration = Duration::from_millis(100);

/// How long to wait in total. A restart loads only the zones the daemon keeps in
/// its own list, so ten seconds is generous; the bound exists so a daemon that
/// never binds is an error and not a hung rpc.
const DEADLINE: Duration = Duration::from_secs(10);

/// Waits until the daemon answers a status request on its control channel, up to
/// [`DEADLINE`].
///
/// A `Type=simple` unit's start returns once the process has been forked, so
/// the first ask can land before the daemon has bound its control port; one
/// failed ask is therefore not an answer, only the whole deadline passing is.
/// The deadline is checked against the host's monotonic clock and not against a
/// count of pauses, because an ask can itself take seconds.
///
/// # Errors
///
/// Returns [`DnsError::SpawnFailed`] when `rndc` cannot be started at all and
/// [`DnsError::Bound`] when one ask outruns its deadline; a daemon that merely
/// does not answer yet is `Ok(false)`.
pub(crate) fn wait_for_control_channel(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
) -> Result<bool, DnsError> {
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let arguments = rndc_arguments(&port, &["status"]);
    let rndc = distro.dns_control_binary();
    let started = host.monotonic_now();
    loop {
        if host.run(&rndc, &arguments, CallClass::Query)?.status == 0 {
            return Ok(true);
        }
        if host.monotonic_now().saturating_sub(started) >= DEADLINE {
            return Ok(false);
        }
        host.pause(STEP);
    }
}

#[cfg(test)]
#[path = "../tests/dns/wait_for_control_channel_tests.rs"]
mod tests;
