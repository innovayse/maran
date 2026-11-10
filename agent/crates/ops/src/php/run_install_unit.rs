//! The names of the installer's two root package units, and starting them.
//!
//! The units are templates shipped by the installer
//! (`installer/systemd/maran-app-install@.service`, `maran-app-remove@.service`); the text
//! between a prefix and `.service` is the instance, which the unit hands to the family's
//! package manager as the whole package argument. The names are Maran facts that are the same
//! on every family, so they are constants here and not adapter methods, and
//! `scripts/lib/check-structure.sh` (check 27) reads them from this file to hold the installer
//! to the same spelling. Shared by installing a PHP version, installing a PHP extension and the
//! app store (`crate::apps`): all hand the unit one package that the ADAPTER named.

use maran_distro::DistroAdapter;

use crate::packages::{PackageError, run_package_unit};
use crate::safe_write::ConfigHost;

/// What the install unit's name starts with.
const INSTALL_UNIT_PREFIX: &str = "maran-app-install@";

/// What the removal unit's name starts with.
const REMOVE_UNIT_PREFIX: &str = "maran-app-remove@";

/// Starts the install unit for `package` and waits for its run to end, returning the unit's
/// name.
///
/// `package` MUST come from the adapter for a validated id or version (`php_package`,
/// `php_extension_package`, `app_package`), never from a caller.
///
/// # Errors
///
/// As [`run_package_unit`].
pub(crate) fn run_install_unit<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    package: &str,
) -> Result<String, PackageError> {
    run_package_unit(host, distro, INSTALL_UNIT_PREFIX, package)
}

/// Starts the removal unit for `package` and waits for its run to end, returning the unit's
/// name. The same rule for `package` as [`run_install_unit`].
///
/// # Errors
///
/// As [`run_package_unit`].
pub(crate) fn run_remove_unit<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    package: &str,
) -> Result<String, PackageError> {
    run_package_unit(host, distro, REMOVE_UNIT_PREFIX, package)
}

#[cfg(test)]
#[path = "../tests/packages/run_install_unit_tests.rs"]
mod tests;
