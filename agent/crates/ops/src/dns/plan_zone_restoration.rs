//! Deciding, from the files on the host, how each zone is served again.

use std::collections::BTreeMap;

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;

use crate::dns::dns_error::DnsError;
use crate::dns::keyed_zones::keyed_zones;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_placement::ZonePlacement;
use crate::dns::zone_host::ZoneHost;

/// Every zone with a file in either directory, sorted by name, and how the files
/// say it is to be served.
///
/// A zone whose file is in the signed directory and whose key is complete is
/// [`ZonePlacement::Signed`]; one whose file is in the signed directory without a
/// complete key is [`ZonePlacement::KeyMissing`]; one that is only in the plain
/// directory is [`ZonePlacement::Unsigned`]. A zone with a file in BOTH (a crash
/// between the write of the new file and the removal of the old) is signed when a
/// complete key is there and unsigned otherwise. That is a guess about which copy
/// was meant, and it is only a start-up guess: the panel sends every zone's stored
/// truth with the next `PutZone`, which moves it to the right place.
///
/// Names that are not exactly `<zone>.db` are skipped, in either directory, which
/// is also why the daemon's `.signed` and `.jnl` files beside a signed zone are
/// never read as zones.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when a directory exists and cannot
/// be listed.
pub(crate) fn plan_zone_restoration(
    host: &dyn ZoneHost,
    directories: &ZoneDirectories,
) -> Result<Vec<(ZoneFileName, ZonePlacement)>, DnsError> {
    let keyed = keyed_zones(host, directories.keys())?;

    // The apex text keys the map, and `(in the plain directory, in the signed one)`
    // is what is known about it.
    let mut found: BTreeMap<String, (ZoneFileName, bool, bool)> = BTreeMap::new();
    for (signed_directory, directory) in [
        (false, directories.zones()),
        (true, directories.signed_zones()),
    ] {
        for name in host.zone_file_names(directory)? {
            let Ok(file) = ZoneFileName::parse(&name) else {
                continue;
            };
            let entry = found
                .entry(file.zone().as_str().to_owned())
                .or_insert_with(|| (file, false, false));
            if signed_directory {
                entry.2 = true;
            } else {
                entry.1 = true;
            }
        }
    }

    Ok(found
        .into_iter()
        .map(|(apex, (file, plain, signed))| {
            let placement = if signed && keyed.contains(&apex) {
                ZonePlacement::Signed
            } else if plain {
                ZonePlacement::Unsigned
            } else {
                ZonePlacement::KeyMissing
            };
            (file, placement)
        })
        .collect())
}

#[cfg(test)]
#[path = "../tests/dns/plan_zone_restoration_tests.rs"]
mod tests;
