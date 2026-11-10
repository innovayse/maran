//! Asking the daemon how it signs one zone.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_host::DnsHost;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::parse_showzone_mode::parse_showzone_mode;
use crate::dns::rndc_arguments::rndc_arguments;

/// The signing mode the daemon's own statement for `zone` shows (`rndc
/// showzone`), or `None` when it could not be read.
///
/// Read-only, and infallible by design: every way the question can fail — the
/// daemon refusing, `rndc` not starting, a deadline, a statement that is not
/// understood — is the same answer, "not observed", because the callers act on it
/// the same way. `put_zone` takes the command that works whatever the statement
/// was, and `GetZone` reports the mode as not observed. A failure is never turned
/// into `Unsigned`.
#[must_use]
pub(crate) fn read_zone_mode(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
    zone: &ZoneName,
) -> Option<SigningMode> {
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let arguments = rndc_arguments(&port, &["showzone", zone.as_str()]);
    let outcome = host
        .run(&distro.dns_control_binary(), &arguments, CallClass::Query)
        .ok()?;

    if outcome.status != 0 {
        return None;
    }

    parse_showzone_mode(&outcome.stdout)
}

#[cfg(test)]
#[path = "../tests/dns/read_zone_mode_tests.rs"]
mod tests;
