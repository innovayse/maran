//! Filesystem locations on the RHEL family.

/// Absolute path of the shell given to an account that must not log in.
///
/// The path RHEL documents. `/usr/sbin/nologin` also resolves on RHEL 8 and
/// later through the merged-/usr symlink, but a symlink that happens to exist
/// is not a contract — the documented path is.
#[must_use]
pub fn nologin_shell() -> &'static str {
    "/sbin/nologin"
}

/// Pool directory for a PHP version, e.g. `/etc/opt/remi/php83/php-fpm.d`.
///
/// Remi drops the dot from the version and roots its packages under
/// `/etc/opt/remi`, so neither half of the Debian path survives.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    format!("/etc/opt/remi/php{}/php-fpm.d", version.replace('.', ""))
}
