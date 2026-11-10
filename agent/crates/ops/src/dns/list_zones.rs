//! The zones the daemon serves.

use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::list_zone_files::list_zone_files;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::zone_host::ZoneHost;

/// The apex names of the zones that have a file AND that the daemon serves,
/// sorted. A file the daemon does not serve is not listed: the listing is the
/// daemon's, and the file only says where to look.
///
/// # Errors
///
/// As [`list_zone_files`].
pub fn list_zones(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directories: &ZoneDirectories,
) -> Result<Vec<ZoneName>, DnsError> {
    Ok(list_zone_files(host, distro, directories)?
        .into_iter()
        .filter(|listed| listed.served)
        .map(|listed| listed.zone)
        .collect())
}

#[cfg(test)]
#[path = "../tests/dns/list_zones_tests.rs"]
mod tests;
