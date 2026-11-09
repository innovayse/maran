//! Starting the installer's root install unit for one package, and reading its
//! failure back.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::php::classify_install_failure::classify_install_failure;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::SafeWriteError;

/// The service-manager subcommand that runs a unit and waits for it to end.
///
/// For a `Type=oneshot` unit without `RemainAfterExit` it returns only when
/// the run is over and exits non-zero when the run failed, which is what makes
/// the exit status the whole verdict. `--no-block` is never passed: it would
/// return before the package manager had done anything.
const START_SUBCOMMAND: &str = "start";

/// What the install unit's name starts with.
///
/// The unit is a template shipped by the installer
/// (`installer/systemd/maran-php-install@.service`); the text between this
/// prefix and [`INSTALL_UNIT_SUFFIX`] is its instance, which the unit hands
/// to the family's package manager as the whole package argument. A Maran
/// fact that is the same on every family, so it is a constant here and not an
/// adapter method.
const INSTALL_UNIT_PREFIX: &str = "maran-php-install@";

/// What the install unit's name ends with.
const INSTALL_UNIT_SUFFIX: &str = ".service";

/// The name of the install unit instance for `package`.
///
/// `package` is the adapter's spelling of a package name, which only ever
/// holds lowercase letters, digits, `-` and one `.`, so it needs no escaping
/// and is used byte for byte.
fn install_unit_name(package: &str) -> String {
    format!("{INSTALL_UNIT_PREFIX}{package}{INSTALL_UNIT_SUFFIX}")
}

/// Describes how a failed run of `unit` ended, for an operator's log.
///
/// Names the unit so that the package manager's own output can be read with
/// `journalctl -u`, and says what a missing unit means.
fn describe_failure(unit: &str, start_stderr: &str) -> String {
    format!(
        "{} (unit {unit}; its package manager output is in `journalctl -u {unit}`; \
         a unit that is not found means the installer must be re-run)",
        start_stderr.trim()
    )
}

/// Starts the install unit for `package` and waits for its run to end,
/// returning the unit's name.
///
/// Shared by installing a PHP version and installing a PHP extension: both
/// hand the unit one package that the ADAPTER named, never one a caller
/// supplied, and both read a failure back the same way, so the start and its
/// classification live here once (rules/rust.md, the rule of two).
///
/// `package` MUST come from the adapter (`php_package`, `php_extension_package`)
/// for a validated version or a catalogue extension: the unit installs whatever
/// its instance names, so what reaches this function is the agent's package
/// list. The instance is a string that cannot start with `-`, hold a slash, or
/// carry anything a unit name needs escaped, and reaches the service manager as
/// one argv element, so there is no command line for anything to re-parse
/// (rules/security.md item 3).
///
/// # Errors
///
/// Returns [`PhpOpError::PackageSourceRefused`],
/// [`PhpOpError::PackageSourceUnreachable`] or [`PhpOpError::PackageManager`]
/// as `classify_install_failure` decides when the unit's run failed, and
/// [`PhpOpError::PackageManager`] when the service manager cannot be started.
/// Returns [`PhpOpError::Bound`] when the service manager outlived its deadline
/// and was killed: the unit lives in the service manager and may still finish,
/// so a bound is NOT a `PackageManager` failure, which would deny that.
pub(crate) fn run_install_unit(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    package: &str,
) -> Result<String, PhpOpError> {
    let unit = install_unit_name(package);

    // `PackageInstall`, the one class that allows for a package manager: `systemctl start` of
    // a `Type=oneshot` unit BLOCKS until the install ends, and its deadline sits above the
    // unit's own `TimeoutStartSec`. A kill at the agent's deadline cancels nothing in the
    // service manager, it only stops the agent waiting, so a bound here is reported as
    // `Bound` and NOT as `PackageManager`: the install may still finish and the version may
    // still appear, which is exactly what `PackageManager` would deny.
    //
    // `--` before the unit: `systemctl` reads its options with `getopt_long`, which ends
    // option parsing at `--`.
    let service_manager = distro.service_manager();
    let outcome = host
        .run(
            &service_manager,
            &[START_SUBCOMMAND, END_OF_OPTIONS, &unit],
            CallClass::PackageInstall,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PhpOpError::Bound(bound),
            other => PhpOpError::PackageManager {
                stderr: describe_failure(&unit, &other.to_string()),
            },
        })?;
    if outcome.status != 0 {
        return Err(classify_install_failure(
            host,
            distro,
            &unit,
            describe_failure(&unit, &outcome.stderr),
        ));
    }

    Ok(unit)
}
