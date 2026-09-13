//! InstallPhpVersion: adding one of the supported versions to this host.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::list_php_versions::is_installed;
use crate::php::supported_versions::ensure_supported;
use crate::php::{PhpHost, PhpOpError};

/// The package manager subcommand that installs a package.
///
/// Spelled the same by both families' package managers, which is why it is a
/// literal here and the BINARY is not: `apt-get` and `dnf` agree on
/// `install -y <package>`, and they disagree on where they live. Only the
/// second is a platform fact (rules/rust.md "Distro adapter").
const INSTALL_SUBCOMMAND: &str = "install";

/// The flag that answers "yes" to a package manager's prompts.
///
/// Not politeness: the agent has no terminal, so an unattended run that
/// prompts hangs until its RPC deadline instead of failing.
const ASSUME_YES: &str = "-y";

/// The service-manager subcommand that makes a unit start at boot.
const ENABLE_SUBCOMMAND: &str = "enable";

/// The flag that also starts the unit now, rather than only at the next boot.
const START_NOW: &str = "--now";

/// Installs `version`'s php-fpm from the family's repository, reporting
/// progress as it goes.
///
/// The supported set is closed (spec §11): `version` is checked against it
/// HERE, and a version outside it never becomes a package name. That is the
/// point of the check rather than a nicety — handing an unrecognised string to
/// `apt-get install` would make the caller the author of the agent's package
/// list, and the agent distrusts the caller (rules/security.md item 12).
///
/// Idempotent, and visibly so: a version already installed completes
/// immediately at 100% rather than re-running the package manager. The panel
/// retries after a timeout, and a retry that ran `apt-get install` again would
/// take the package database lock and stall for minutes to achieve nothing.
///
/// `progress` is called with a percentage and a stage name — `"preparing"`,
/// `"download"`, `"install"`, `"enable"` — which the service layer turns into
/// the stream's `Progress` messages. It is a callback rather than a channel so
/// that this function stays synchronous and testable, and so that an operation
/// nobody is streaming still runs.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] when `version` is not one of the
/// supported ones, [`PhpOpError::PackageManager`] when the package manager
/// cannot be run or refuses the installation, and
/// [`PhpOpError::ServiceEnable`] when the package installed but its service
/// could not be enabled — reported separately because the two need different
/// things from an operator: a repository to fix, or a unit to look at.
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

    // Checked before any work: this is the retry path, and it must cost one
    // `stat` rather than one package-manager run.
    if is_installed(host, distro, version) {
        progress(100, "install");
        return Ok(());
    }

    // Named "preparing" and not "repository": a stage name is a claim about
    // work, and an operator watching a stalled install would go and look at
    // Sury or Remi for a step that never ran. The repository is provisioned
    // once by the installer, not per version. If it ever becomes lazy, the
    // command belongs exactly here, under a name that then earns itself.
    progress(10, "preparing");

    let package = distro.php_package(version.as_str());
    progress(30, "download");

    // One argv array, no shell (rules/security.md item 3). `package` is
    // derived by the adapter from a `PhpVersion` that has been through
    // `ensure_supported`, so it is one of six known strings — but it reaches
    // `execve` as its own argument regardless, so there is no command line for
    // anything to re-parse even if that ever stops being true.
    let outcome = host
        .run(
            distro.package_manager(),
            &[INSTALL_SUBCOMMAND, ASSUME_YES, &package],
        )
        .map_err(|error| PhpOpError::PackageManager {
            stderr: error.to_string(),
        })?;
    if outcome.status != 0 {
        return Err(PhpOpError::PackageManager {
            stderr: outcome.stderr,
        });
    }
    progress(80, "install");

    // Enabled AND started: a version installed but not running is a pool whose
    // socket never appears, which surfaces as a 502 on the first site pointed
    // at it rather than as a failure of this operation.
    let service = distro.php_fpm_service(version.as_str());
    let outcome = host
        .run(
            distro.service_manager(),
            &[ENABLE_SUBCOMMAND, START_NOW, &service],
        )
        .map_err(|error| PhpOpError::ServiceEnable {
            stderr: error.to_string(),
        })?;
    if outcome.status != 0 {
        return Err(PhpOpError::ServiceEnable {
            stderr: outcome.stderr,
        });
    }

    progress(100, "enable");
    Ok(())
}

#[cfg(test)]
#[path = "../tests/php/install_php_version_tests.rs"]
mod tests;
