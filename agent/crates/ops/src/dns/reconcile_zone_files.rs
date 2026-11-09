//! Making the daemon serve every zone the agent has a file for.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::model::zone_reconciliation::ZoneReconciliation;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::zone_host::ZoneHost;

/// Adds to the daemon every zone that has a file and that it does not serve.
///
/// The zone list is the daemon's own (`allow-new-zones`); the files are
/// derivable from the database. They can differ when the daemon's list was lost
/// (a state directory restored from backup, a reinstall) and this puts them
/// back. It asks the daemon for each zone and never reads the daemon's private
/// zone-list files, whose format depends on the build.
///
/// A daemon that does not answer is not an error: the host has simply not
/// enabled DNS, and the answer is [`ZoneReconciliation::DaemonNotAnswering`]. A
/// zone the daemon refuses to add is counted as failed and the pass goes on, so
/// one bad file does not hide the others.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when the directory cannot be
/// listed and [`DnsError::SpawnFailed`] or [`DnsError::Bound`] when `rndc` cannot
/// run.
pub fn reconcile_zone_files(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directory: &Path,
) -> Result<ZoneReconciliation, DnsError> {
    let mut zones: Vec<_> = host
        .zone_file_names(directory)?
        .iter()
        .filter_map(|name| ZoneFileName::parse(name).ok())
        .collect();
    zones.sort_by(|a, b| a.as_str().cmp(b.as_str()));

    let rndc = distro.dns_control_binary();
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let (mut added, mut failed) = (0_usize, 0_usize);

    for file in &zones {
        let reading = match get_zone(host, distro, file.zone()) {
            Ok(reading) => reading,
            Err(DnsError::ControlRefused { .. }) => {
                return Ok(ZoneReconciliation::DaemonNotAnswering);
            }
            Err(other) => return Err(other),
        };
        if matches!(reading, ZoneReading::Served { .. }) {
            continue;
        }

        let Some(path) = directory.join(file.as_str()).to_str().map(str::to_owned) else {
            failed += 1;
            continue;
        };
        let statement = format!("{{ type primary; file \"{path}\"; }};");
        let arguments = rndc_arguments(&port, &["addzone", file.zone().as_str(), &statement]);
        let outcome = host.run(&rndc, &arguments, CallClass::Reload)?;
        if outcome.status == 0 {
            added += 1;
        } else {
            failed += 1;
        }
    }

    Ok(ZoneReconciliation::Compared {
        files: zones.len(),
        added,
        failed,
    })
}

#[cfg(test)]
#[path = "../tests/dns/reconcile_zone_files_tests.rs"]
mod tests;
