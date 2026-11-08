//! InstallPhpVersion: adding one of the supported versions to this host.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::list_php_versions::is_installed;
use crate::php::run_install_unit::run_install_unit;
use crate::php::supported_versions::{ensure_installable, ensure_supported};
use crate::php::write_opcache_drop_in::write_opcache_drop_in;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::SafeWriteError;
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The service-manager subcommand that makes a unit start at boot.
const ENABLE_SUBCOMMAND: &str = "enable";

/// The flag that also starts the unit now, rather than only at the next boot.
const START_NOW: &str = "--now";

/// Installs `version`'s php-fpm by starting the install unit for its package,
/// reporting progress as it goes.
///
/// The package manager is NOT run by the agent. Its own sandbox keeps `/usr`
/// read-only, so a package install in it fails on the first file; instead the
/// agent asks the service manager to start `maran-php-install@<package>`, a
/// templated root unit the installer ships, and waits for that run to end. The
/// package manager then runs in the unit's own cgroup and mount namespace.
/// There is no fallback: a host whose installer has not been re-run has no
/// unit, and the failure says so rather than running the package manager here.
///
/// The supported set is closed (spec §11): `version` is checked against it
/// HERE, and a version outside it never becomes a unit name. That is the whole
/// enforcement of the closed set — the unit installs whatever package it is
/// named after, so it is this check, not the unit, that stops the caller from
/// being the author of the agent's package list (rules/security.md item 12).
///
/// Idempotent, and visibly so: a version already installed completes
/// immediately at 100% rather than starting the unit. The panel retries after
/// a timeout, and a retry that ran the package manager again would take the
/// package database lock and stall for minutes to achieve nothing. Success is
/// also checked rather than assumed: after the unit succeeds the pool
/// directory must exist, which catches a package manager that matched some
/// other package by regular expression (it retries a name nothing has exactly
/// as an unanchored pattern, and the `.` in `php8.3-fpm` matches any
/// character) and a package that installed without the directory this panel
/// looks for.
///
/// `progress` is called with a percentage and a stage name — `"preparing"`,
/// `"download"`, `"install"`, `"enable"` — which the service layer turns into
/// the stream's `Progress` messages. The stages are markers around one
/// blocking call, not measurements of the package manager. It is a callback
/// rather than a channel so that this function stays synchronous and
/// testable, and so that an operation nobody is streaming still runs.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] when `version` is not one of the
/// supported ones, [`PhpOpError::PackageSourceRefused`] when the unit's package
/// manager failed because a package source answered with an HTTP error,
/// [`PhpOpError::PackageSourceUnreachable`] when it failed because no source
/// could be reached, [`PhpOpError::PackageManager`] when the install unit cannot
/// be started, fails for any other reason, or succeeds without the version
/// appearing (each carries the service manager's stderr and the unit name), and [`PhpOpError::ServiceEnable`]
/// when the package installed but its service could not be enabled — reported
/// separately because the two need different things from an operator: a
/// repository or the unit's journal to look at, or a service unit to fix.
/// Returns [`PhpOpError::Bound`] when the service manager outlived its deadline
/// at either step and was killed, which is neither of those: the install unit
/// runs in the service manager and may still finish, so the caller reads the
/// host again before it believes the version is absent. The last step, and the
/// early return for an installed version, write the OPcache drop-in
/// (`write_opcache_drop_in`) and return its errors unchanged
/// (`PoolValidation`, `ReloadFailed`, `Render`, `ConfigWrite`, `Bound`).
pub fn install_php_version<P>(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
    mut progress: P,
) -> Result<(), PhpOpError>
where
    P: FnMut(u32, &str),
{
    ensure_supported(version)?;
    ensure_installable(distro, version)?;

    // Checked before any work: this is the retry path, and it must cost one
    // `stat` rather than one unit run.
    if is_installed(host, distro, version) {
        // The retry path converges the OPcache drop-in too: a version installed
        // before the drop-in existed, or by an earlier attempt that stopped
        // between the package and the drop-in, gets it here. It costs one read
        // when the file is already current.
        write_opcache_drop_in(host, distro, version)?;
        progress(100, "install");
        return Ok(());
    }

    // Named "preparing" and not "repository": a stage name is a claim about
    // work, and an operator watching a stalled install would go and look at
    // Sury or Remi for a step that never ran. The host's own repositories
    // supply the package; nothing here configures them.
    progress(10, "preparing");

    // The package is the adapter's name for a version that has passed
    // `ensure_supported`; starting the unit for it and classifying its failure
    // is shared with the extension install (`run_install_unit`).
    let package = distro.php_package(version.as_str());
    progress(30, "download");
    let unit = run_install_unit(host, distro, &package)?;

    if !is_installed(host, distro, version) {
        return Err(PhpOpError::PackageManager {
            stderr: format!(
                "the install unit {unit} succeeded but PHP {} is still not installed; \
                 its package manager output is in `journalctl -u {unit}`",
                version.as_str()
            ),
        });
    }
    progress(80, "install");

    let service_manager = distro.service_manager();
    // Enabled AND started: a version installed but not running is a pool whose
    // socket never appears, which surfaces as a 502 on the first site pointed
    // at it rather than as a failure of this operation.
    let service = distro.php_fpm_service(version.as_str());
    let outcome = host
        .run(
            &service_manager,
            &[ENABLE_SUBCOMMAND, START_NOW, END_OF_OPTIONS, &service],
            CallClass::Reload,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PhpOpError::Bound(bound),
            other => PhpOpError::ServiceEnable {
                stderr: other.to_string(),
            },
        })?;
    if outcome.status != 0 {
        return Err(PhpOpError::ServiceEnable {
            stderr: outcome.stderr,
        });
    }

    // The hardened OPcache drop-in, once the version's service runs: the write
    // validates with the version's own php-fpm and reloads it. A refusal fails the
    // install and the panel's retry converges it through the early return above.
    write_opcache_drop_in(host, distro, version)?;

    progress(100, "enable");
    Ok(())
}

#[cfg(test)]
#[path = "../tests/php/install_php_version_tests.rs"]
mod tests;
