//! Starting one of the installer's root package units for one package.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::packages::PackageError;
use crate::packages::classify_package_unit_failure::classify_package_unit_failure;
use crate::safe_write::{ConfigHost, SafeWriteError};

/// The service-manager subcommand that runs a unit and waits for it to end.
///
/// For a `Type=oneshot` unit without `RemainAfterExit` it returns only when the run is over
/// and exits non-zero when the run failed, which is what makes the exit status the whole
/// verdict. `--no-block` is never passed: it would return before the package manager had done
/// anything.
const START_SUBCOMMAND: &str = "start";

/// What a unit's name ends with.
const UNIT_SUFFIX: &str = ".service";

/// Describes how a failed run of `unit` ended, for an operator's log.
fn describe_failure(unit: &str, start_stderr: &str) -> String {
    format!(
        "{} (unit {unit}; its package manager output is in `journalctl -u {unit}`; \
         a unit that is not found means the installer must be re-run)",
        start_stderr.trim()
    )
}

/// Starts the template unit named by `prefix` for `package` and waits for its run to end,
/// returning the unit's name.
///
/// `prefix` is one of the two constants of `php/run_install_unit.rs`, and `package` MUST come
/// from the adapter for a validated id: the unit installs or removes whatever its instance
/// names, so what reaches this function is the agent's package list, never a caller's. The
/// instance cannot start with `-`, hold a slash or carry anything a unit name needs escaped,
/// and reaches the service manager as one argv element (rules/security.md item 3).
///
/// # Errors
///
/// Returns the classification of `classify_package_unit_failure` when the run failed,
/// [`PackageError::PackageManager`] when the service manager cannot be started, and
/// [`PackageError::Bound`] when it outlived its deadline: the unit lives in the service manager
/// and may still finish, so a bound is not a `PackageManager` failure, which would deny that.
pub(crate) fn run_package_unit<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    prefix: &str,
    package: &str,
) -> Result<String, PackageError> {
    let unit = format!("{prefix}{package}{UNIT_SUFFIX}");

    // `PackageInstall`, the one class that allows for a package manager. A kill at the agent's
    // deadline cancels nothing in the service manager. `--` before the unit: `systemctl` ends
    // option parsing there.
    let service_manager = distro.service_manager();
    let outcome = host
        .run(
            &service_manager,
            &[START_SUBCOMMAND, END_OF_OPTIONS, &unit],
            CallClass::PackageInstall,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PackageError::Bound(bound),
            other => PackageError::PackageManager {
                stderr: describe_failure(&unit, &other.to_string()),
            },
        })?;
    if outcome.status != 0 {
        return Err(classify_package_unit_failure(
            host,
            distro,
            &unit,
            package,
            describe_failure(&unit, &outcome.stderr),
        ));
    }

    Ok(unit)
}
