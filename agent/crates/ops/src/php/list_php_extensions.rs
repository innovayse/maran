//! ListPhpExtensions: which catalogue extensions one PHP version loads, and
//! what the host can do with each.

use std::collections::BTreeSet;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::list_php_versions::is_installed;
use crate::php::model::extension_availability::ExtensionAvailability;
use crate::php::model::php_extension::PhpExtension;
use crate::php::supported_versions::ensure_supported;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::SafeWriteError;

/// The argument that makes php-fpm print the modules it has loaded and exit.
const LIST_MODULES_ARGUMENT: &str = "-m";

/// Lists the thirteen catalogue extensions of `version`, alphabetical, one
/// entry each.
///
/// The state is what the daemon loads and not what a file says: ONE bounded
/// run of `php-fpm<version> -m`, whose listing names the modules (`mysqli`,
/// `Zend OPcache`), is compared with each catalogue name's
/// `loaded_module_name`, case-insensitively. A directory listing of ini files
/// would report an intention, not an observation (rules/testing.md).
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] for a version outside the
/// supported set, [`PhpOpError::PhpVersionNotInstalled`] when it is supported
/// but absent, [`PhpOpError::ExtensionProbeFailed`] when the module listing
/// cannot be started or exits non-zero (an empty list would read every
/// extension as off, so a failed probe is never an empty answer), and
/// [`PhpOpError::Bound`] when the probe outlived its deadline and was killed.
pub fn list_php_extensions(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> Result<Vec<PhpExtension>, PhpOpError> {
    let modules = loaded_modules(host, distro, version)?;

    Ok(PhpExtensionName::catalogue()
        .map(|name| extension_reading(distro, version, &modules, name))
        .collect())
}

/// Reads the lower-cased names of the modules `version`'s php-fpm has loaded.
///
/// Checks the version first, so every caller refuses an unsupported or absent
/// version the same way. Lines are trimmed; blank lines and the `[PHP Modules]`
/// and `[Zend Modules]` section headers are not modules and are skipped. A
/// module's name can hold a space (`Zend OPcache`), so a line is one name, never
/// split.
///
/// # Errors
///
/// The same as [`list_php_extensions`].
pub(crate) fn loaded_modules(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> Result<BTreeSet<String>, PhpOpError> {
    ensure_supported(version)?;
    if !is_installed(host, distro, version) {
        return Err(PhpOpError::PhpVersionNotInstalled {
            version: version.as_str().to_owned(),
        });
    }

    // `Validate`, the short class: `php-fpm -m` loads the configuration, prints
    // and exits, changing nothing.
    let outcome = host
        .run(
            &distro.php_fpm_binary(version),
            &[LIST_MODULES_ARGUMENT],
            CallClass::Validate,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PhpOpError::Bound(bound),
            other => PhpOpError::ExtensionProbeFailed {
                stderr: other.to_string(),
            },
        })?;
    if outcome.status != 0 {
        return Err(PhpOpError::ExtensionProbeFailed {
            stderr: outcome.stderr,
        });
    }

    Ok(outcome
        .stdout
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !(line.starts_with('[') && line.ends_with(']')))
        .map(str::to_lowercase)
        .collect())
}

/// The reading of one catalogue extension: whether `modules` holds it, and what
/// the family can do with it.
///
/// Availability is the adapter's two answers and nothing else: no package means
/// [`ExtensionAvailability::NotPackaged`] (the agent cannot turn it on), a
/// package without a switch means [`ExtensionAvailability::DisableNotSupported`],
/// and both means [`ExtensionAvailability::Available`].
pub(crate) fn extension_reading(
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
    modules: &BTreeSet<String>,
    name: PhpExtensionName,
) -> PhpExtension {
    let availability = if distro.php_extension_package(version, name).is_none() {
        ExtensionAvailability::NotPackaged
    } else if distro.php_extension_switch(version, name).is_none() {
        ExtensionAvailability::DisableNotSupported
    } else {
        ExtensionAvailability::Available
    };

    PhpExtension {
        name,
        enabled: modules.contains(&name.loaded_module_name().to_lowercase()),
        availability,
    }
}

#[cfg(test)]
#[path = "../tests/php/list_php_extensions_tests.rs"]
mod tests;
