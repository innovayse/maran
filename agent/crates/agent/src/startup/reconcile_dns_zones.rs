//! The startup pass that makes the DNS daemon serve every zone the agent has a
//! file for.

use std::path::Path;

use maran_distro::DistroAdapter;
use maran_ops::dns::{ZoneHost, ZoneReconciliation, reconcile_zone_files};

/// Adds to the DNS daemon every zone that has a file and that it does not serve,
/// at startup, and logs what it found.
///
/// # Why it exists
///
/// The daemon keeps its own list of zones (`rndc addzone`), and the zone files
/// are derivable data. When the list is lost — a state directory restored from a
/// backup, a daemon reinstalled — the files are still there and the zones are not
/// served. This pass puts them back from the files, asking the daemon for each
/// zone and never reading its private list.
///
/// # Why it never refuses the start
///
/// The trade `reconcile_web_tree` is decided by: this process is the operator's
/// only remote repair tool, and a zone that is not served is a fault to report,
/// not a reason to take every other rpc down. A host that has not enabled DNS has
/// no daemon to ask; that is the ordinary state, logged at debug, and a daemon
/// enabled later is reached by the next `PutZone` of each zone.
///
/// Answers the outcome, or `None` when the zone directory could not be read, so a
/// test can observe the result without reading the log.
pub(crate) fn reconcile_dns_zones(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
) -> Option<ZoneReconciliation> {
    let directory = Path::new(distro.dns_zone_directory());

    match reconcile_zone_files(host, distro, directory) {
        Ok(ZoneReconciliation::DaemonNotAnswering) => {
            tracing::debug!(
                "the DNS daemon does not answer, so no zone file was compared with it \
                 (DNS is not enabled on this host, or the daemon has not started yet)"
            );
            Some(ZoneReconciliation::DaemonNotAnswering)
        }
        Ok(
            outcome @ ZoneReconciliation::Compared {
                files,
                added,
                failed,
            },
        ) => {
            if failed > 0 {
                tracing::warn!(
                    files,
                    added,
                    failed,
                    "the DNS daemon refused to serve some zone files; they are not served"
                );
            } else {
                tracing::info!(files, added, "DNS zone files were compared with the daemon");
            }
            Some(outcome)
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the DNS zone files could not be reconciled with the daemon, so a zone \
                 whose daemon-side entry was lost may not be served"
            );
            None
        }
    }
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_dns_zones_tests.rs"]
mod tests;
