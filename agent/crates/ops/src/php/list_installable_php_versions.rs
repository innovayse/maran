//! ListInstallablePhpVersions: which versions the operator may install here.

use maran_distro::DistroAdapter;

use crate::php::list_php_versions::is_installed;
use crate::php::model::installable_php_version::InstallablePhpVersion;
use crate::php::supported_versions::{SUPPORTED_VERSIONS, ensure_installable};
use crate::php::{PhpHost, PhpOpError};
use maran_agent_core::validation::web::php_version::PhpVersion;

/// Lists the PHP versions that can be installed on this host, newest first,
/// each saying whether it already is.
///
/// A version is installable when it is in the panel's closed supported set
/// AND the family's repository publishes it (`DistroAdapter::installable_php_versions`);
/// both checks are the ones `install_php_version` applies, so the list never
/// offers a version the install would refuse. Installed versions stay in the
/// list with `installed` set rather than disappearing, so an operator sees
/// the whole picture and the panel can mark them.
///
/// Installed-ness is the pool-directory check shared with `list_php_versions`,
/// a `stat` per version and never a package-manager query (see that function
/// for why). Newest first comes from reversing the fixed oldest-first list, not
/// from sorting version text.
///
/// # Errors
///
/// Returns [`PhpOpError`] for no reason today; the signature is fallible so a
/// future implementation that must ask the machine something dearer than a
/// `stat` does not become a breaking change for its callers.
pub fn list_installable_php_versions(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<InstallablePhpVersion>, PhpOpError> {
    let versions = SUPPORTED_VERSIONS
        .iter()
        .rev()
        .filter_map(|text| {
            // The supported list holds only well-formed versions; a failed
            // parse would mean the constant was edited wrongly, and the
            // version is then simply not offered.
            let version = PhpVersion::parse(text).ok()?;
            ensure_installable(distro, &version).ok()?;

            Some(InstallablePhpVersion {
                version: (*text).to_owned(),
                installed: is_installed(host, distro, &version),
            })
        })
        .collect();

    Ok(versions)
}

#[cfg(test)]
#[path = "../tests/php/list_installable_php_versions_tests.rs"]
mod tests;
