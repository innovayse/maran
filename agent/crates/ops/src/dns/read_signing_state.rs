//! Observing how the daemon signs one zone.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_host::DnsHost;
use crate::dns::model::key_publication::KeyPublication;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_signing_observation::ZoneSigningObservation;
use crate::dns::parse_signing_list::parse_signing_list;
use crate::dns::read_zone_mode::read_zone_mode;
use crate::dns::rndc_arguments::rndc_arguments;

/// What the daemon says about how it signs `zone`: the mode its own statement for
/// the zone shows (`rndc showzone`) and, for a signed or withdrawing zone, the key
/// it reports having finished signing with (`rndc signing -list`).
///
/// Observation and nothing else (rules/rust.md "Config writes"): neither fact is
/// read from the files this agent wrote or from what the panel last sent. Both
/// answers are parsed through printable-token checks and never echoed. `dig` is
/// not assumed to be on the host, so what a resolver would be told is NOT
/// observed here, only what the daemon's control channel says about itself.
///
/// Infallible: every failure to ask or to understand is
/// [`ZoneSigningObservation::NOT_OBSERVED`] for that fact. The signing list is
/// not asked when the mode is unsigned (the answer is the absence of a key) and
/// not asked when the mode could not be read (there is no basis to say which
/// question applies).
#[must_use]
pub(crate) fn read_signing_state(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
    zone: &ZoneName,
) -> ZoneSigningObservation {
    let mode = read_zone_mode(host, distro, zone);

    let key = match mode {
        None => KeyPublication::NotObserved,
        Some(SigningMode::Unsigned) => KeyPublication::NotPublished,
        Some(SigningMode::Signed | SigningMode::Withdrawing) => {
            read_published_key(host, distro, zone)
        }
    };

    ZoneSigningObservation { mode, key }
}

/// Asks the daemon which key has finished signing `zone`.
fn read_published_key(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
    zone: &ZoneName,
) -> KeyPublication {
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let arguments = rndc_arguments(&port, &["signing", "-list", zone.as_str()]);
    let Ok(outcome) = host.run(&distro.dns_control_binary(), &arguments, CallClass::Query) else {
        return KeyPublication::NotObserved;
    };

    if outcome.status != 0 {
        return KeyPublication::NotObserved;
    }

    parse_signing_list(&outcome.stdout)
}

#[cfg(test)]
#[path = "../tests/dns/read_signing_state_tests.rs"]
mod tests;
