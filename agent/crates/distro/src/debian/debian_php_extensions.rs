//! PHP extension packages, switches and ini directory on the Debian family.
//!
//! Everything here is the author's reading of Sury's package list and of the
//! `phpenmod` / `phpdismod` tools, NOT checked against a live repository or
//! host; the open measurements are in
//! `docs/superpowers/notes/2026-11-07-php-extensions-threat-note.md` ("Not
//! verified by the author"). No fixtures were recorded, because the session
//! that wrote this had no container and no network.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::adapter_binary::AdapterBinary;
use crate::php_extension_switch::PhpExtensionSwitch;

/// The OS package that provides `extension` for `version`, e.g. `php8.3-gd`.
///
/// Sury ships one package per extension named `php<version>-<extension>`,
/// the version keeping its dot, and the catalogue's names are the suffixes
/// (`mysql` -> `php8.3-mysql`, `xml` -> `php8.3-xml`, `opcache` ->
/// `php8.3-opcache`, `imagick` -> `php8.3-imagick`). Every catalogue entry has
/// one on this family, so the answer is always `Some`. The mapping is the
/// author's reading, NOT checked against a live repository.
#[must_use]
pub fn php_extension_package(version: &PhpVersion, extension: PhpExtensionName) -> Option<String> {
    Some(format!("php{}-{}", version.as_str(), extension.as_str()))
}

/// Absolute path of `phpenmod`, which switches a module on for one version
/// and SAPI.
///
/// `php-common` installs it at `/usr/sbin/phpenmod`. Reading, not measured.
#[must_use]
pub fn phpenmod_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/phpenmod")
}

/// Absolute path of `phpdismod`, which switches a module off for one version
/// and SAPI.
///
/// `php-common` installs it beside `phpenmod`; the tool does the opposite
/// action because of the name it is started under, which is why it is its own
/// program and not a flag. Reading, not measured.
#[must_use]
pub fn phpdismod_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/phpdismod")
}

/// The ini module name `phpenmod` and `phpdismod` take for `extension`.
///
/// They address the ini file under `mods-available`, which is the module's own
/// name for every catalogue entry except `mysql`: the MySQL driver package
/// ships `mysqli.ini` and no `mysql.ini`, so the switch is aimed at `mysqli`.
/// Whether the sibling `pdo_mysql` ini follows is not measured here.
fn ini_module_name(extension: PhpExtensionName) -> &'static str {
    match extension.as_str() {
        "mysql" => "mysqli",
        other => other,
    }
}

/// The argument vector shared by enabling and disabling: the version, the
/// fpm SAPI and the module, built from validated values only.
fn switch_arguments(version: &PhpVersion, extension: PhpExtensionName) -> Vec<String> {
    vec![
        "-v".to_owned(),
        version.as_str().to_owned(),
        "-s".to_owned(),
        "fpm".to_owned(),
        ini_module_name(extension).to_owned(),
    ]
}

/// How to switch `extension` on and off for the fpm SAPI of `version`:
/// `phpenmod -v 8.3 -s fpm gd` and `phpdismod -v 8.3 -s fpm gd`.
///
/// Always `Some` on this family. The argument vector holds only the validated
/// version and the catalogue's module name. Reading, NOT checked against a
/// live host; in particular whether a package upgrade re-enables a disabled
/// module is unmeasured.
#[must_use]
pub fn php_extension_switch(
    version: &PhpVersion,
    extension: PhpExtensionName,
) -> Option<PhpExtensionSwitch> {
    Some(PhpExtensionSwitch {
        enable_program: phpenmod_binary(),
        enable_arguments: switch_arguments(version, extension),
        disable_program: phpdismod_binary(),
        disable_arguments: switch_arguments(version, extension),
    })
}

/// Directory of the additional ini files php-fpm reads for `version`, e.g.
/// `/etc/php/8.3/fpm/conf.d`.
///
/// Sury keeps one tree per version and SAPI and the version keeps its dot, as
/// in [`crate::debian::debian_paths::php_fpm_pool_directory`]. Reading, NOT
/// checked against a live host.
#[must_use]
pub fn php_ini_drop_in_directory(version: &PhpVersion) -> String {
    format!("/etc/php/{}/fpm/conf.d", version.as_str())
}

#[cfg(test)]
#[path = "../tests/debian/debian_php_extensions_tests.rs"]
mod tests;
