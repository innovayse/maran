//! Deciding what a failed run of the PHP install unit was.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroAdapter, PackageFailure};

use crate::php::model::install_run::InstallRun;
use crate::php::{PhpHost, PhpOpError};

/// The service-manager subcommand that prints a unit's properties.
const SHOW_SUBCOMMAND: &str = "show";

/// The run's id, which the journal files every line of it under.
const INVOCATION_ID_PROPERTY: &str = "--property=InvocationID";

/// The exit status of the run's main process, the package manager.
const EXEC_MAIN_STATUS_PROPERTY: &str = "--property=ExecMainStatus";

/// Print each journal line's message and nothing else: the package manager's
/// own text, byte for byte, which is what the classification reads.
const MESSAGE_ONLY: &str = "--output=cat";

/// Never hand the output to a pager; there is no terminal to page on.
const NO_PAGER: &str = "--no-pager";

/// The journal field every line a unit's processes wrote carries, naming the
/// run that wrote it.
const INVOCATION_MATCH: &str = "_SYSTEMD_INVOCATION_ID=";

/// Turns a failed run of `unit` into the failure the caller reports, carrying
/// `report` — the service manager's own account of the failure — for an
/// operator's log.
///
/// The package manager runs in the installer's unit and not as the agent's
/// child, so the agent learns what it printed only by asking: the service
/// manager names the run and its exit status, and the journal holds that
/// run's output and no other run's. Both are then read by
/// [`DistroAdapter::classify_package_failure`], the one place a package
/// manager's failure is classified (issue #251):
///
/// - a source that answered with an HTTP error is
///   [`PhpOpError::PackageSourceRefused`];
/// - a source nobody could reach is [`PhpOpError::PackageSourceUnreachable`];
/// - everything else is [`PhpOpError::PackageManager`].
///
/// When the run cannot be read back — the service manager names no run, the
/// unit does not exist, or either reader fails or outlives its deadline — the
/// failure is still the install's and stays [`PhpOpError::PackageManager`],
/// with the reason the run could not be read appended to `report`: the
/// classification is the failure's detail, never a second failure in its
/// place. A journal line the journal had not yet stored when it was read is
/// the stated limit of this reading, and it errs the same way — towards this
/// host rather than towards a mirror.
pub(crate) fn classify_install_failure(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    unit: &str,
    report: String,
) -> PhpOpError {
    match read_package_failure(host, distro, unit) {
        Ok(PackageFailure::SourceRefused) => PhpOpError::PackageSourceRefused { stderr: report },
        Ok(PackageFailure::SourceUnreachable) => {
            PhpOpError::PackageSourceUnreachable { stderr: report }
        }
        Ok(PackageFailure::Local) => PhpOpError::PackageManager { stderr: report },
        Err(unread) => PhpOpError::PackageManager {
            stderr: format!("{report}; the run could not be classified: {unread}"),
        },
    }
}

/// Reads the last run of `unit` back from the service manager and its
/// journal and asks the adapter what kind of failure it was.
///
/// # Errors
///
/// Returns, as text for an operator's log, why the run could not be read: the
/// service manager could not be asked or named no complete run of the unit, or
/// the journal reader could not be started, exited non-zero, or was killed at
/// its deadline.
fn read_package_failure(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    unit: &str,
) -> Result<PackageFailure, String> {
    let shown = host
        .run(
            &distro.service_manager(),
            &[
                SHOW_SUBCOMMAND,
                INVOCATION_ID_PROPERTY,
                EXEC_MAIN_STATUS_PROPERTY,
                END_OF_OPTIONS,
                unit,
            ],
            CallClass::Query,
        )
        .map_err(|error| error.to_string())?;
    if shown.status != 0 {
        return Err(format!("`systemctl show` exited {}", shown.status));
    }
    let run = InstallRun::parse(&shown.stdout)
        .ok_or_else(|| "the service manager names no finished run of the unit".to_owned())?;

    let invocation = format!("{INVOCATION_MATCH}{}", run.invocation_id());
    let journal = host
        .run(
            &distro.journal_binary(),
            &[MESSAGE_ONLY, NO_PAGER, &invocation],
            CallClass::Query,
        )
        .map_err(|error| error.to_string())?;
    if journal.status != 0 {
        return Err(format!("`journalctl` exited {}", journal.status));
    }

    Ok(distro.classify_package_failure(run.exit_status(), &journal.stdout))
}

#[cfg(test)]
#[path = "../tests/php/classify_install_failure_tests.rs"]
mod tests;
