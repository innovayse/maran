//! Asking the daemon whether it serves a zone.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::command_output_excerpt::command_output_excerpt;
use crate::dns::dns_error::DnsError;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::parse_zone_serial::parse_zone_serial;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::zone_host::ZoneHost;

/// Reports what the daemon says about `zone`, read from the daemon over its
/// control channel (`rndc zonestatus`) and never from the file on disk or from
/// what the panel last sent: a file is an intention, the daemon's answer is an
/// observation (rules/rust.md "Config writes").
///
/// Read-only. Whether the daemon does not know the zone is told from a
/// transport failure by the tool's own `not found`; any other non-zero answer —
/// a daemon that is not running, a refused key — is an error, because reading it
/// as "not served" would let a stopped daemon authorise recreating every zone.
///
/// # Errors
///
/// Returns [`DnsError::ControlRefused`] when the daemon cannot be asked,
/// [`DnsError::ZoneStateUnreadable`] when it answered success without a serial,
/// [`DnsError::SpawnFailed`] when `rndc` could not be started and
/// [`DnsError::Bound`] when it outran its deadline.
pub fn get_zone(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    zone: &ZoneName,
) -> Result<ZoneReading, DnsError> {
    let rndc = distro.dns_control_binary();
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let arguments = rndc_arguments(&port, &["zonestatus", zone.as_str()]);

    let outcome = host.run(&rndc, &arguments, CallClass::Query)?;
    if outcome.status == 0 {
        return parse_zone_serial(&outcome.stdout)
            .map(|serial| ZoneReading::Served { serial })
            .ok_or(DnsError::ZoneStateUnreadable);
    }

    let output = command_output_excerpt(&outcome);
    if output.to_ascii_lowercase().contains("not found") {
        return Ok(ZoneReading::NotServed);
    }

    Err(DnsError::ControlRefused { output })
}

#[cfg(test)]
#[path = "../tests/dns/get_zone_tests.rs"]
mod tests;
