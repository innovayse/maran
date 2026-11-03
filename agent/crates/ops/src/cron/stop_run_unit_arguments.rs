//! The `systemctl` argument vector that stops the unit a run by hand started.

use maran_agent_core::end_of_options::END_OF_OPTIONS;

use crate::cron::model::run_unit_name::RunUnitName;

/// `systemctl`'s verb for ending a unit.
const STOP: &str = "stop";

/// The arguments of `systemctl stop` for `unit` (issue #336).
///
/// Called when a ceiling ended a run by hand: the output ceiling kills the
/// `systemd-run` CLIENT, and the client's death does not end the job, which the
/// service manager keeps running until its own `RuntimeMaxSec`. The panel tells
/// the customer the run was stopped, so the agent has to make that true.
///
/// **The vector is the verb, the end-of-options marker and the unit's full
/// name, and nothing else.** The name is a [`RunUnitName`], built by the agent
/// from the account's uid, a validated entry id and a counter, so no request
/// text reaches it. The program is not chosen here: `ProcessCronHost` starts
/// the distro adapter's `service_manager`, a type only the `distro` crate can
/// construct, with these arguments and no shell (rules/security.md items 3
/// and 12).
///
/// The unit's `TimeoutStopSec` is set to a few seconds when it is started
/// (`run_now_arguments`), so a job that ignores `SIGTERM` is killed within that
/// time and the stop returns after it.
pub(crate) fn stop_run_unit_arguments(unit: &RunUnitName) -> [String; 3] {
    [STOP.to_owned(), END_OF_OPTIONS.to_owned(), unit.service()]
}
