//! Package and binary names on the RHEL family.

/// Name of the OS package that provides php-fpm for `version`, e.g.
/// `php83-php-fpm`.
///
/// Remi drops the dot from the version, matching the RPM naming convention
/// the rest of its PHP packages use, and correspondingly in
/// [`crate::rhel::rhel_paths::php_fpm_pool_directory`].
#[must_use]
pub fn php_package(version: &str) -> String {
    format!("php{}-php-fpm", version.replace('.', ""))
}

/// Absolute path of the package manager binary.
///
/// Remi's repository is installed and driven through `dnf`, the RHEL
/// family's package manager (spec §4).
#[must_use]
pub fn package_manager() -> &'static str {
    "/usr/bin/dnf"
}
