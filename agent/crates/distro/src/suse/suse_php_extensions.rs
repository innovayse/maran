//! PHP extension packages, switches and ini directory on the SUSE family.
//!
//! Everything here is the author's reading of the distribution's `php8` package family, NOT
//! checked against a live repository or host. Nothing was measured and no fixtures were recorded:
//! the session that wrote this had no SUSE container. Each answer below carries the command that
//! confirms it in the pinned openSUSE Leap image.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php_extension_switch::PhpExtensionSwitch;
use crate::suse::suse_packages;

/// The OS package that provides `extension` for `version`: `php8-<extension>` for the one version
/// the distribution ships, and a name no repository carries for every other.
///
/// This family offers ONE PHP minor from its own repositories (see
/// [`suse_packages::php_package`]), whose packages share the unversioned `php8-` prefix, so the
/// catalogue name is the suffix with no exception: `mysql` is `php8-mysql`, `zip` is `php8-zip`.
/// Every catalogue extension therefore has a package of its own and the answer is never `None`.
/// Any other version is answered with the dotted `php<version>-<extension>`, which no SUSE
/// repository carries, so an extension of a version this family does not offer cannot be
/// installed by accident into the one it does.
///
/// UNVERIFIED: read from the distribution's package naming, NOT checked against a repository.
/// The names most likely to differ are `xml` (the distribution may split it into `php8-xmlreader`
/// and `php8-xmlwriter`), `mysql` (`php8-mysql`) and `opcache` (it may be built into `php8`
/// itself); confirm every name with
/// `zypper --non-interactive --no-refresh search --match-exact -t package php8-<extension>` in the
/// pinned Leap image. A name that is wrong ends an install as `PackageFailure::NotAvailable`, never
/// as a wrong package.
#[must_use]
pub fn php_extension_package(version: &PhpVersion, extension: PhpExtensionName) -> Option<String> {
    let version = version.as_str();
    let name = extension.as_str();
    if version == suse_packages::DISTRIBUTION_PHP_VERSION {
        Some(format!("php8-{name}"))
    } else {
        Some(format!("php{version}-{name}"))
    }
}

/// How to switch `extension` on and off: there is no clean way on this family.
///
/// The distribution's extension packages ship plain ini files under the drop-in directory and no
/// `phpdismod`-style tool, and no mechanism was measured, so the answer is `None` for every
/// extension. An extension can be switched on by installing its package; the agent answers a typed
/// refusal for off rather than renaming or editing the distribution's ini files. UNVERIFIED:
/// confirm with `rpm -ql php8-gd` and `command -v phpdismod` in the pinned Leap image.
#[must_use]
pub fn php_extension_switch(
    _version: &PhpVersion,
    _extension: PhpExtensionName,
) -> Option<PhpExtensionSwitch> {
    None
}

/// Directory of the additional ini files php-fpm reads: `/etc/php8/conf.d`.
///
/// The directory does not depend on the version, because the family offers exactly one PHP, the
/// distribution's `php8`; the argument is taken for the shape every family shares. Reading, NOT
/// checked against a live host: confirm with `php-fpm -i | grep -i 'Scan this dir'` in the pinned
/// Leap image after installing `php8-fpm`.
#[must_use]
pub fn php_ini_drop_in_directory(_version: &PhpVersion) -> String {
    "/etc/php8/conf.d".to_owned()
}

#[cfg(test)]
#[path = "../tests/suse/suse_php_extensions_tests.rs"]
mod tests;
