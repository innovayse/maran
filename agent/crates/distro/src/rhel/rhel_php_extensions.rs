//! PHP extension packages, switches and ini directory on the RHEL family.
//!
//! Everything here is the author's reading of Remi's package list, NOT checked
//! against a live repository or host; the open measurements are in
//! `docs/superpowers/notes/2026-11-07-php-extensions-threat-note.md` ("Not
//! verified by the author"). No fixtures were recorded, because the session
//! that wrote this had no container and no network.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php_extension_switch::PhpExtensionSwitch;

/// The one PHP version whose Remi redis build is the 5 series; every later
/// version's is the 6 series.
const REDIS_FIVE_VERSION: &str = "7.4";

/// The OS package that provides `extension` for `version`, e.g.
/// `php83-php-gd`.
///
/// Remi drops the dot from the version and names a version's packages
/// `php<vv>-php-<extension>`. Four catalogue names differ from that shape:
/// `mysql` is `php<vv>-php-mysqlnd`, `zip` is `php<vv>-php-pecl-zip`, `redis`
/// is `php<vv>-php-pecl-redis6` (`php<vv>-php-pecl-redis5` for 7.4) and
/// `imagick` is `php<vv>-php-pecl-imagick-im7`. `curl` has no package of its
/// own — it is built into `php-common` — so the answer is `None`. The mapping
/// is the author's reading, NOT checked against a live repository; Remi moves
/// its `pecl` names between releases.
#[must_use]
pub fn php_extension_package(version: &PhpVersion, extension: PhpExtensionName) -> Option<String> {
    let prefix = format!("php{}-php", version.as_str().replace('.', ""));

    let suffix = match extension.as_str() {
        "curl" => return None,
        "mysql" => "mysqlnd",
        "zip" => "pecl-zip",
        "redis" if version.as_str() == REDIS_FIVE_VERSION => "pecl-redis5",
        "redis" => "pecl-redis6",
        "imagick" => "pecl-imagick-im7",
        other => other,
    };

    Some(format!("{prefix}-{suffix}"))
}

/// How to switch `extension` on and off: there is no clean way on this family.
///
/// Remi's packages ship plain ini files and no `phpdismod`, and no mechanism
/// was measured, so the answer is `None` for every extension. An extension can
/// be switched on by installing its package; the agent answers a typed
/// refusal for off rather than renaming or editing the distribution's ini
/// files. This is the fallback of owner decision O5, applied because the
/// measurement is open.
#[must_use]
pub fn php_extension_switch(
    _version: &PhpVersion,
    _extension: PhpExtensionName,
) -> Option<PhpExtensionSwitch> {
    None
}

/// Directory of the additional ini files php-fpm reads for `version`, e.g.
/// `/etc/opt/remi/php83/php.d`.
///
/// Remi drops the dot from the version and roots its packages under
/// `/etc/opt/remi`, as in [`crate::rhel::rhel_paths::php_fpm_pool_directory`].
/// Reading, NOT checked against a live host.
#[must_use]
pub fn php_ini_drop_in_directory(version: &PhpVersion) -> String {
    format!(
        "/etc/opt/remi/php{}/php.d",
        version.as_str().replace('.', "")
    )
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_php_extensions_tests.rs"]
mod tests;
