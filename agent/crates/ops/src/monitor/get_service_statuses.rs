//! GetServiceStatuses: what the service manager says about the units the panel
//! watches.

use maran_distro::DistroAdapter;

use crate::monitor::model::service_state::ServiceState;
use crate::monitor::model::service_status::ServiceStatus;
use crate::monitor::model::unit_report::UnitReport;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// The reporting subcommand. It prints properties and changes nothing.
const SHOW: &str = "show";

/// The end of `systemctl`'s options, after which every argument is a unit.
///
/// Defence in depth, and cheap. The unit names this area asks about come from
/// the `DistroAdapter`'s closed set — except one, the socket name read out of a
/// unit's own `TriggeredBy` property, which is checked only for a `.socket`
/// suffix. A unit called `-H.socket` satisfies that suffix and would reach
/// `systemctl` looking exactly like its `-H` option, which takes a remote host
/// to talk to. The string is root-controlled today, so this is not a hole; it
/// is one token that means nobody has to keep proving it is not one.
const END_OF_OPTIONS: &str = "--";

/// The properties one call asks for.
const PROPERTIES: [&str; 4] = [
    "--property=LoadState",
    "--property=ActiveState",
    "--property=SubState",
    "--property=TriggeredBy",
];

/// `LoadState` for a unit this host has no unit file for.
const NOT_FOUND: &str = "not-found";

/// `ActiveState` for a unit that is up.
const ACTIVE: &str = "active";

/// `ActiveState` for a unit that is up and re-reading its configuration.
const RELOADING: &str = "reloading";

/// `ActiveState` for a unit that is not running.
const INACTIVE: &str = "inactive";

/// `ActiveState` for a unit that tried to run and gave up.
const FAILED: &str = "failed";

/// `ActiveState` for a unit on its way up.
const ACTIVATING: &str = "activating";

/// `ActiveState` for a unit on its way down.
const DEACTIVATING: &str = "deactivating";

/// Reads the state of every unit the panel watches, in the adapter's fixed
/// order.
///
/// The set of units is closed and comes from `DistroAdapter::managed_units` —
/// no rpc anywhere accepts a unit name, so nothing a caller supplies is ever
/// handed to the service manager.
///
/// # A service that is down is an ANSWER
///
/// The only error this function returns is a failure to REACH the service
/// manager. A unit that is stopped, failed, or not installed at all comes back
/// as a [`ServiceStatus`] saying so, because that is precisely the fact the
/// caller asked for. An implementation that returned `Err` for a stopped
/// service would have inverted its own purpose: the panel would show a broken
/// monitor where it asked to be shown a broken service.
///
/// # Errors
///
/// Returns [`MonitorError::ServiceManagerUnavailable`] when the service manager
/// cannot be started or refuses the query — which, on a host whose service
/// manager is not running, is the honest answer for all four units at once.
pub fn get_service_statuses(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<ServiceStatus>, MonitorError> {
    let mut statuses = Vec::new();

    for unit in distro.managed_units() {
        let report = show(host, distro, unit)?;
        let (state, detail) = classify(host, distro, &report)?;

        statuses.push(ServiceStatus {
            unit: unit.to_owned(),
            state,
            detail,
        });
    }

    Ok(statuses)
}

/// Asks the service manager for one unit's properties.
///
/// # Errors
///
/// Returns [`MonitorError::ServiceManagerUnavailable`] when the tool cannot be
/// started or exits non-zero. A non-zero exit here really is a failure of the
/// query rather than news about the unit: `show` reports on a unit that does
/// not exist by printing `LoadState=not-found` and exiting zero, which is what
/// makes "no such unit" an answer instead of an error.
fn show(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
    unit: &str,
) -> Result<UnitReport, MonitorError> {
    // Options first, then the separator, then the unit — in that order and no
    // other. `--` ends option parsing, so a `--property=` written after it
    // would be taken as a second unit name rather than as a request for a
    // property.
    let mut arguments = vec![SHOW];
    arguments.extend_from_slice(&PROPERTIES);
    arguments.push(END_OF_OPTIONS);
    arguments.push(unit);

    let outcome = host.run(distro.service_manager(), &arguments)?;
    if outcome.status != 0 {
        return Err(MonitorError::ServiceManagerUnavailable {
            code: outcome.status,
        });
    }

    Ok(UnitReport::parse(&outcome.stdout))
}

/// Turns one unit's properties into a state and a sentence about it.
///
/// # Socket activation, and why an inactive unit is not automatically stopped
///
/// On the Debian family the ENABLED unit is `ssh.socket`, not `ssh.service`:
/// the socket holds the listening descriptor and hands it to one long-running
/// `sshd -D` the first time somebody connects. Until that first connection the
/// service is inactive on a host whose SSH is listening and completely healthy,
/// and because the socket declares `Accept=no` the window closes at the first
/// login and never reopens. Calling that state "stopped" would invent an SSH
/// outage on every Debian-family host at every reboot, and the panel's alerting
/// would mail an operator about each one. So the SOCKET is asked whether it is
/// listening, and a unit waiting behind a listening socket is reported as not
/// yet started — which is [`ServiceState::Unknown`], not
/// [`ServiceState::Stopped`].
///
/// The RHEL family enables `sshd.service` directly and does not enable its
/// socket at all, so it has no such window and the same code answers it
/// correctly without knowing which family it is on: a unit with no listening
/// socket behind it and an inactive state IS stopped.
///
/// A state this function does not recognise becomes `Unknown` rather than
/// `Stopped`, for the same reason: an unfamiliar word from a future systemd is
/// not evidence of an outage.
///
/// # Errors
///
/// Returns [`MonitorError::ServiceManagerUnavailable`] when a triggering socket
/// cannot be asked about.
fn classify(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
    report: &UnitReport,
) -> Result<(ServiceState, String), MonitorError> {
    if report.load_state == NOT_FOUND {
        return Ok((
            ServiceState::Unknown,
            "the unit is not installed on this host".to_owned(),
        ));
    }

    match report.active_state.as_str() {
        ACTIVE | RELOADING => Ok((ServiceState::Running, describe(report))),
        FAILED => Ok((ServiceState::Stopped, describe(report))),
        ACTIVATING | DEACTIVATING => Ok((ServiceState::Unknown, describe(report))),
        INACTIVE => inactive(host, distro, report),
        _ => Ok((ServiceState::Unknown, describe(report))),
    }
}

/// Decides what an inactive unit means, by asking its sockets.
///
/// # Errors
///
/// Returns [`MonitorError::ServiceManagerUnavailable`] when a socket cannot be
/// asked about.
fn inactive(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
    report: &UnitReport,
) -> Result<(ServiceState, String), MonitorError> {
    for socket in report.triggering_sockets() {
        if show(host, distro, socket)?.active_state == ACTIVE {
            return Ok((
                ServiceState::Unknown,
                format!("not yet started; {socket} is listening for it"),
            ));
        }
    }

    Ok((ServiceState::Stopped, describe(report)))
}

/// The unit's own two words for what it is doing.
///
/// systemd's vocabulary and nothing else — never a tool's standard error, which
/// this area's status type must not carry.
fn describe(report: &UnitReport) -> String {
    format!("{} ({})", report.active_state, report.sub_state)
}

#[cfg(test)]
#[path = "../tests/monitor/get_service_statuses_tests.rs"]
mod tests;
