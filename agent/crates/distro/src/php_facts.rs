//! The php-fpm side of a family: pool directory, unit, binary, package and installable versions.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::adapter_binary::AdapterBinary;

/// The php-fpm side of a family: pool directory, unit, binary, package and installable versions.
pub trait PhpFacts: Send + Sync {
    /// Directory the php-fpm pool files for `version` live in.
    ///
    /// The families disagree twice over: the path shape, and how the version
    /// appears in it — `/etc/php/8.3/fpm/pool.d` against
    /// `/etc/opt/remi/php83/php-fpm.d`, where the dot is dropped.
    fn php_fpm_pool_directory(&self, version: &str) -> String;

    /// Name of the php-fpm service unit for `version`.
    ///
    /// Systemd unit names mirror the package name on each family — `php8.3-fpm`
    /// against `php83-php-fpm` — so this and [`Self::php_package`] must be
    /// changed together or the two silently drift apart.
    fn php_fpm_service(&self, version: &str) -> String;

    /// Absolute path of the php-fpm binary for `version`, for the
    /// process-execution allow-list.
    ///
    /// Sury and Remi both install a version-suffixed binary rather than a single
    /// `php-fpm`, since several PHP versions run side by side on the same host —
    /// `/usr/sbin/php-fpm8.3` against `/opt/remi/php83/root/usr/sbin/php-fpm`.
    ///
    /// Takes the validated [`PhpVersion`] and not text: this is the one path
    /// built with `format!`, and it is exactly as closed as the version
    /// grammar (two ASCII-digit components), so it cannot carry a separator,
    /// a `..` or a control character.
    fn php_fpm_binary(&self, version: &PhpVersion) -> AdapterBinary;

    /// Name of the OS package that provides php-fpm for `version`.
    ///
    /// Sury's Debian packages keep the dot (`php8.3-fpm`); Remi's RHEL packages
    /// drop it (`php83-php-fpm`), matching the RPM naming convention the rest of
    /// Remi's PHP packages use. Getting this wrong installs nothing and the
    /// version-specific pool directory never appears.
    fn php_package(&self, version: &str) -> String;

    /// The PHP versions this family's repository publishes a php-fpm package
    /// for, oldest first.
    ///
    /// Sury (Debian family) and Remi (RHEL family) each publish their own
    /// range, and a version missing from it has no package to install, so the
    /// answer is the family's and not the panel's. The agent offers the
    /// intersection of this set and its own closed supported set, which is
    /// what the installable-versions query reports and what an install is
    /// checked against.
    fn installable_php_versions(&self) -> &'static [&'static str];
}
