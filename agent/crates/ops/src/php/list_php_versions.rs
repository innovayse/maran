//! ListPhpVersions: which of the supported versions this host has.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::model::installed_php_version::InstalledPhpVersion;
use crate::php::supported_versions::SUPPORTED_VERSIONS;
use crate::php::{PhpHost, PhpOpError};

/// Lists the supported PHP versions installed on this host, newest first.
///
/// Installed-ness is decided by the presence of the version's pool directory,
/// which its `-fpm` package creates and nothing else does. That is a `stat`
/// per supported version — six of them — and deliberately not a package-manager
/// query: the panel calls this on every page that offers a version picker, and
/// `dpkg-query`/`rpm -q` per version per page load takes the package database
/// lock that a real installation elsewhere on the host is trying to hold.
///
/// Newest first because that is the order a picker wants and the order a
/// default is taken from. It comes from reversing the fixed
/// `SUPPORTED_VERSIONS` list rather than from sorting: version strings
/// compare wrongly as text — `"8.10" < "8.9"` — and this is a list a future
/// `8.10` will join.
///
/// # Errors
///
/// Returns [`PhpOpError`] for no reason today — the signature is fallible so
/// that a future implementation which must ask the machine something more
/// expensive than a `stat` does not become a breaking change for every caller.
/// A version whose directory cannot be examined is simply reported as absent,
/// since "not installed" is the honest answer to give a picker.
pub fn list_php_versions(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<InstalledPhpVersion>, PhpOpError> {
    let installed = SUPPORTED_VERSIONS
        .iter()
        .rev()
        .filter_map(|version| {
            let pool_directory = distro.php_fpm_pool_directory(version);
            if !host.directory_exists(Path::new(&pool_directory)) {
                return None;
            }

            Some(InstalledPhpVersion {
                version: (*version).to_owned(),
                pool_directory,
                socket_directory: AgentPaths::PHP_FPM_SOCKET_DIRECTORY.to_owned(),
            })
        })
        .collect();

    Ok(installed)
}

/// Whether `version` is installed on this host.
///
/// The same question [`list_php_versions`] answers for all of them, asked for
/// one — through the same host method, so a version can never be listed as
/// installed by one path and refused as missing by the other.
pub(crate) fn is_installed(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> bool {
    host.directory_exists(Path::new(&distro.php_fpm_pool_directory(version.as_str())))
}

#[cfg(test)]
#[path = "../tests/php/list_php_versions_tests.rs"]
mod tests;
