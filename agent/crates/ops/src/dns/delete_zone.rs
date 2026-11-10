//! Removing one zone and its file.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::command_output_excerpt::command_output_excerpt;
use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::delete_zone_outcome::DeleteZoneOutcome;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::remove_signed_copy::remove_signed_copy;
use crate::dns::remove_signing_keys::remove_signing_keys;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::zone_host::ZoneHost;

/// Takes `zone` out of the daemon and deletes its files: the zone file in the
/// plain directory, the signed zone's file and the files the daemon keeps beside
/// it, and every signing key.
///
/// Idempotent: a zone the daemon does not serve and that has no file is a
/// success that says it was not there, which is what makes a retry after a lost
/// response safe.
///
/// The order is the daemon first, then the file. The other order would leave a
/// daemon serving from memory a zone whose file is gone, which fails to load at
/// its next restart. The cost of this order is a window between the two steps in
/// which a restart finds a zone file the daemon has just forgotten and the
/// start-up reconciliation would load it again; it is closed by repeating the
/// delete, which converges, and by the panel diffing the listing against its
/// own zones. The call needs the daemon to answer: with DNS switched off it
/// refuses rather than removing a file the daemon's own zone list still names.
///
/// A key file never outlives its zone: the private key of a deleted zone is
/// removed with it (`remove_signing_keys`), after the daemon has let go of the zone
/// and so of the key directory. Both directories are always looked at, because a
/// zone's mode is not remembered here and a crash can leave a copy in each.
///
/// Not run under the config-tree lock: removing a zone file changes no
/// configuration the checker reads, and the panel serialises per zone.
///
/// # Errors
///
/// Returns [`DnsError::ControlRefused`] when the daemon cannot be asked or
/// refuses to remove the zone, [`DnsError::ConfigUnreadable`] when the file
/// exists and cannot be read, [`DnsError::ConfigWrite`] when it cannot be
/// removed, [`DnsError::ZoneDirectoryUnreadable`] when the key directory exists and
/// cannot be listed, and [`DnsError::SpawnFailed`] or [`DnsError::Bound`] for `rndc`.
pub fn delete_zone(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directories: &ZoneDirectories,
    zone: &ZoneName,
) -> Result<DeleteZoneOutcome, DnsError> {
    let file_name = ZoneFileName::of(zone);
    let target = directories.zones().join(file_name.as_str());
    let signed_target = directories.signed_zones().join(file_name.as_str());
    let file_present =
        host.read_config(&target)?.is_some() || host.read_config(&signed_target)?.is_some();
    let served = matches!(get_zone(host, distro, zone)?, ZoneReading::Served { .. });

    if served {
        let rndc = distro.dns_control_binary();
        let port = AgentPaths::DNS_CONTROL_PORT.to_string();
        let arguments = rndc_arguments(&port, &["delzone", zone.as_str()]);
        let outcome = host.run(&rndc, &arguments, CallClass::Reload)?;
        if outcome.status != 0 {
            let output = command_output_excerpt(&outcome);
            // A zone that vanished between the question and the command is the
            // goal reached, not a failure.
            if !output.to_ascii_lowercase().contains("not found") {
                return Err(DnsError::ControlRefused { output });
            }
        }
    }

    host.remove_file(&target)?;
    remove_signed_copy(host, directories.signed_zones(), &file_name)?;
    remove_signing_keys(host, directories.keys(), zone, None)?;

    Ok(DeleteZoneOutcome {
        was_present: served || file_present,
    })
}

#[cfg(test)]
#[path = "../tests/dns/delete_zone_tests.rs"]
mod tests;
