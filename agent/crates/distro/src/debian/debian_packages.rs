//! Package and binary names on the Debian family.

/// Name of the OS package that provides php-fpm for `version`, e.g.
/// `php8.3-fpm`.
///
/// Sury's Debian packages keep the dot from the version in both the package
/// name and, correspondingly, in [`crate::debian::debian_paths::php_fpm_pool_directory`].
#[must_use]
pub fn php_package(version: &str) -> String {
    format!("php{version}-fpm")
}

/// Absolute path of the package manager binary.
///
/// Sury's repository is installed and driven through `apt-get`, the Debian
/// family's package manager (spec §4).
#[must_use]
pub fn package_manager() -> &'static str {
    "/usr/bin/apt-get"
}
