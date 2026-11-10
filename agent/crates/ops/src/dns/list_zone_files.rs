//! Reading the zone directory back, with the daemon's opinion of each zone.

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::listed_zone::ListedZone;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::zone_host::ZoneHost;

/// Lists every zone the agent keeps a file for, in the plain directory or the
/// signed one, sorted by name and once each, with whether the daemon currently
/// serves it.
///
/// An entry whose name is not exactly `<zone>.db` as the agent writes it — a
/// stray file, a backup copy, a different spelling — is skipped, never read as a
/// zone. Costs one `rndc` run per zone.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when the directory cannot be
/// listed, and every error of [`get_zone`] for the first zone the daemon cannot
/// be asked about.
pub fn list_zone_files(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directories: &ZoneDirectories,
) -> Result<Vec<ListedZone>, DnsError> {
    let mut zones: Vec<_> = Vec::new();
    for directory in [directories.zones(), directories.signed_zones()] {
        zones.extend(
            host.zone_file_names(directory)?
                .iter()
                .filter_map(|name| ZoneFileName::parse(name).ok())
                .map(|file| file.zone().clone()),
        );
    }
    zones.sort_by(|a, b| a.as_str().cmp(b.as_str()));
    zones.dedup();

    zones
        .into_iter()
        .map(|zone| {
            let served = matches!(get_zone(host, distro, &zone)?, ZoneReading::Served { .. });
            Ok(ListedZone { zone, served })
        })
        .collect()
}

#[cfg(test)]
#[path = "../tests/dns/list_zone_files_tests.rs"]
mod tests;
