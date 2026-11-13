//! The names of the installer's two root Node.js module units, and starting them.
//!
//! A family that ships Node.js as module streams (`dnf module install nodejs:20/common`) cannot
//! use the package units: the module is enabled and installed as one step, and a stream is
//! switched by resetting the module first. The units are templates shipped by the installer
//! (`installer/systemd/maran-app-nodejs-install@.service`, `maran-app-nodejs-remove@.service`);
//! the text between a prefix and `.service` is the stream's instance, its major in digits, which
//! the unit hands to the package manager as part of one module specification. The names are Maran
//! facts, so they are constants here, and `scripts/lib/check-structure.sh` (check 27) reads them
//! from this file to hold the installer to the same spelling.

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_distro::DistroAdapter;

use crate::packages::{PackageError, run_unit_reporting};
use crate::safe_write::ConfigHost;

/// What the Node.js module install unit's name starts with.
const INSTALL_UNIT_PREFIX: &str = "maran-app-nodejs-install@";

/// What the Node.js module removal unit's name starts with.
const REMOVE_UNIT_PREFIX: &str = "maran-app-nodejs-remove@";

/// Starts the install unit of `stream` and waits for its run to end, returning the unit's name.
///
/// `stream` is a [`NodejsModuleStream`], which only a validated Node.js major can make, so the
/// instance is digits and never a caller's text.
///
/// # Errors
///
/// As [`run_package_unit`](crate::packages::run_package_unit), a classified failure naming
/// the stream's spec (`nodejs:20/common`) and not its digits.
pub(crate) fn run_nodejs_install_unit<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    stream: &NodejsModuleStream,
) -> Result<String, PackageError> {
    run_unit_reporting(
        host,
        distro,
        INSTALL_UNIT_PREFIX,
        stream.instance(),
        &stream.spec(),
    )
}

/// Starts the removal unit of `stream` and waits for its run to end, returning the unit's name.
/// The same rule for `stream` as [`run_nodejs_install_unit`].
///
/// # Errors
///
/// As [`run_package_unit`](crate::packages::run_package_unit), a classified failure naming
/// the stream's spec (`nodejs:20/common`) and not its digits.
pub(crate) fn run_nodejs_remove_unit<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    stream: &NodejsModuleStream,
) -> Result<String, PackageError> {
    run_unit_reporting(
        host,
        distro,
        REMOVE_UNIT_PREFIX,
        stream.instance(),
        &stream.spec(),
    )
}

#[cfg(test)]
#[path = "../tests/packages/run_nodejs_module_unit_tests.rs"]
mod tests;
