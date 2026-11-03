//! ListPendingUpdates: which packages does the host already know a newer version of?

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroAdapter, PendingUpdate, PendingUpdatesFailure};

use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Lists the packages the host's package manager already knows a newer version
/// of, from its local cache alone (issue #324).
///
/// # Why this exists
///
/// A server whose security updates are pending is a finding for its
/// administrator, and nothing on the panel reported it. This asks the package
/// manager the question and tells the panel the answer.
///
/// # What it runs, and what that cannot do
///
/// One program the adapter names ([`maran_distro::PackageFacts::package_query_binary`]),
/// with constant arguments the adapter chooses and no caller value: a
/// simulated upgrade on the Debian family and a cache-only `check-update` on
/// the RHEL family. Neither installs, removes, configures or refreshes
/// anything, and neither makes an outbound call: a host that never refreshes its
/// own metadata reports a frozen list, which is the right answer to "what does
/// this host already know". The family that cannot tell a never-built
/// catalogue from an up-to-date host asks one more constant question first
/// ([`maran_distro::PackageFacts::catalogue_probe`]).
///
/// Every run is the bounded `CallClass::Query`, whose deadline and output cap
/// are the spawn core's and are not overridden here. This operation takes no
/// lock: it waits on nothing the agent holds, so it cannot stall another
/// operation.
///
/// # Errors
///
/// Returns [`MonitorError::UpdateCatalogueUnavailable`] when the package
/// manager could not be started, exited with a status that is not a success,
/// printed a line the adapter's reader does not know, or the catalogue was
/// never built. Returns [`MonitorError::Bound`] when a run was killed at its
/// deadline or wrote more than the host will read. **None of these is an empty
/// list:** an `Ok` with no updates is only ever the answer to a read that
/// succeeded, and each path above is checked separately by a test.
pub fn list_pending_updates(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<PendingUpdate>, MonitorError> {
    let program = distro.package_query_binary();

    if let Some(probe) = distro.catalogue_probe() {
        let outcome = run_query(host, &program, probe.arguments)?;
        (probe.read)(outcome.status, &outcome.stdout).map_err(unavailable)?;
    }

    let outcome = run_query(host, &program, distro.pending_updates_arguments())?;
    distro
        .read_pending_updates(outcome.status, &outcome.stdout)
        .map_err(unavailable)
}

/// Runs the package-query program with `arguments` in the `Query` class.
///
/// # Errors
///
/// Returns [`MonitorError::UpdateCatalogueUnavailable`] with
/// [`PendingUpdatesFailure::NotStarted`] when the program could not be started,
/// and passes [`MonitorError::Bound`] through unchanged: a killed run has said
/// nothing about the host, and it is never folded into either "not started" or
/// "no updates".
fn run_query(
    host: &dyn MonitorHost,
    program: &AdapterBinary,
    arguments: &[&str],
) -> Result<CommandOutcome, MonitorError> {
    host.run(program, arguments, CallClass::Query)
        .map_err(|error| match error {
            MonitorError::ServiceManagerUnavailable { .. } => {
                unavailable(PendingUpdatesFailure::NotStarted)
            }
            other => other,
        })
}

/// Wraps a reader's failure as this operation's error.
fn unavailable(reason: PendingUpdatesFailure) -> MonitorError {
    MonitorError::UpdateCatalogueUnavailable { reason }
}

#[cfg(test)]
#[path = "../tests/monitor/list_pending_updates_tests.rs"]
mod tests;
