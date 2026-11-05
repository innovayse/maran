//! GetRebootRequirement: does the host need a reboot to run what is installed?

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroAdapter, RebootProbe, RebootRequirement};

use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Asks the host's own probe whether a reboot is needed to run what is already
/// installed (issue #163).
///
/// # Why this exists
///
/// A kernel or library update that is installed but not yet running protects
/// nobody, and unattended security updates never reboot on their own. The
/// panel shows the answer so the operator can choose the moment.
///
/// # What it runs, and what that cannot do
///
/// Whatever [`maran_distro::PackageFacts::reboot_probe`] names: the presence of
/// one marker file, or one program with constant arguments and no operand, run
/// in the bounded `CallClass::Query` (deadline and output cap are the spawn
/// core's, not overridden here). Nothing is written, no lock is taken, and no
/// caller value reaches a path or an argument. Asking twice returns the same
/// answer for the same host state.
///
/// # Errors
///
/// Returns [`MonitorError::RebootProbeNotRun`] when the marker could not be
/// examined (anything but "no such entry") or the probe program could not be
/// started, [`MonitorError::RebootProbeFailed`] when the program exited with a
/// status the family's reader gives no answer to, and [`MonitorError::Bound`]
/// when it was killed at its deadline or wrote more than the host will read.
/// **None of these is [`RebootRequirement::NotRequired`]:** a probe that did not
/// run has not said the host is safe to leave, and each path is checked
/// separately by a test.
pub fn get_reboot_requirement(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<RebootRequirement, MonitorError> {
    match distro.reboot_probe() {
        RebootProbe::Marker(path) => Ok(if host.marker_present(&path)? {
            RebootRequirement::Required
        } else {
            RebootRequirement::NotRequired
        }),
        RebootProbe::Program { binary, arguments } => {
            let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
            let outcome = host
                .run(&binary, &arguments, CallClass::Query)
                .map_err(|error| match error {
                    MonitorError::ServiceManagerUnavailable { .. } => {
                        MonitorError::RebootProbeNotRun
                    }
                    other => other,
                })?;
            distro
                .read_reboot_probe_run(outcome.status)
                .map_err(|reason| MonitorError::RebootProbeFailed { reason })
        }
    }
}

#[cfg(test)]
#[path = "../tests/monitor/get_reboot_requirement_tests.rs"]
mod tests;
