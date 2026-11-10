//! Making the daemon serve every zone the agent has a file for.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_placement::ZonePlacement;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::model::zone_reconciliation::ZoneReconciliation;
use crate::dns::plan_zone_restoration::plan_zone_restoration;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::zone_host::ZoneHost;
use crate::dns::zone_statement_for::zone_statement_for;

/// Adds to the daemon every zone that has a file and that it does not serve.
///
/// The zone list is the daemon's own (`allow-new-zones`); the files are
/// derivable from the database. They can differ when the daemon's list was lost
/// (a state directory restored from backup, a reinstall) and this puts them
/// back. It asks the daemon for each zone and never reads the daemon's private
/// zone-list files, whose format depends on the build.
///
/// It looks in both directories (`plan_zone_restoration`). A zone found in the
/// signed directory with a complete key is re-added SIGNED, with the same
/// statement `put_zone` uses, so a rebuilt host does not serve unsigned a zone the
/// registrar holds a DS record for. It is re-added as signed, never as
/// withdrawing: the agent keeps no record of the phase, and the panel's own
/// reconciler sends the stored phase with the next `PutZone`. A zone whose file is
/// in the signed directory with NO complete key is counted as failed and left
/// alone — telling the daemon to sign it would have the daemon generate a key the
/// registrar's DS record does not name.
///
/// A daemon that does not answer is not an error: the host has simply not
/// enabled DNS, and the answer is [`ZoneReconciliation::DaemonNotAnswering`]. A
/// zone the daemon refuses to add is counted as failed and the pass goes on, so
/// one bad file does not hide the others.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when a directory cannot be
/// listed and [`DnsError::SpawnFailed`] or [`DnsError::Bound`] when `rndc` cannot
/// run.
pub fn reconcile_zone_files(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directories: &ZoneDirectories,
) -> Result<ZoneReconciliation, DnsError> {
    let zones = plan_zone_restoration(host, directories)?;

    let rndc = distro.dns_control_binary();
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let (mut added, mut failed) = (0_usize, 0_usize);

    for (file, placement) in &zones {
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

        let (directory, mode) = match placement {
            ZonePlacement::Unsigned => (directories.zones(), SigningMode::Unsigned),
            ZonePlacement::Signed => (directories.signed_zones(), SigningMode::Signed),
            ZonePlacement::KeyMissing => {
                tracing::warn!(
                    zone = file.zone().as_str(),
                    "a zone file is in the signed directory and no complete key is: not served"
                );
                failed += 1;
                continue;
            }
        };
        let Ok(statement) =
            zone_statement_for(&directory.join(file.as_str()), directories.keys(), mode)
        else {
            failed += 1;
            continue;
        };
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
