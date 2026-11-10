//! What the daemon says about one zone: whether it serves it and how it signs it.

use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::zone_description::ZoneDescription;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::read_signing_state::read_signing_state;
use crate::dns::zone_host::ZoneHost;

/// Reports whether the daemon serves `zone` and, when it does, at which serial and
/// under which signing, all asked of the daemon over its control channel.
///
/// This is the whole of the `GetZone` rpc. [`get_zone`] stays the serial-only
/// question `put_zone` and the listing ask; the signing facts are asked only here,
/// and only for a zone the daemon serves, since a zone it does not know has no
/// signing to describe. A signing fact the daemon could not answer is reported as
/// not observed (`read_signing_state`), never as an error: the zone is served
/// whether or not its signing could be read.
///
/// # Errors
///
/// Returns every error of [`get_zone`].
pub fn describe_zone(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    zone: &ZoneName,
) -> Result<ZoneDescription, DnsError> {
    match get_zone(host, distro, zone)? {
        ZoneReading::NotServed => Ok(ZoneDescription::NotServed),
        ZoneReading::Served { serial } => Ok(ZoneDescription::Served {
            serial,
            signing: read_signing_state(host, distro, zone),
        }),
    }
}

#[cfg(test)]
#[path = "../tests/dns/describe_zone_tests.rs"]
mod tests;
