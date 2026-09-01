//! Service and binary names on the Debian family.

/// Absolute path of the nginx binary, for the process-execution allow-list.
#[must_use]
pub fn nginx_binary() -> &'static str {
    "/usr/sbin/nginx"
}

/// Name of the nginx systemd service unit.
///
/// Debian's nginx package registers the service under this name; the RHEL
/// family happens to agree, but that is a coincidence worth stating rather
/// than a shared rule this crate relies on.
#[must_use]
pub fn nginx_service() -> &'static str {
    "nginx"
}

/// Absolute path of the service manager binary, for the process-execution
/// allow-list.
///
/// Both families are systemd and both ship it at `/usr/bin/systemctl`, but the
/// value still comes from the adapter rather than a literal in `ops`: a binary
/// is only spawnable because this trait named it, and `ops` naming an absolute
/// path is the violation whether or not the path is currently right
/// (rules/rust.md "Distro adapter").
#[must_use]
pub fn service_manager() -> &'static str {
    "/usr/bin/systemctl"
}

/// The user the web server runs as.
///
/// Debian's nginx package creates and runs as `www-data`, the distribution's
/// long-standing shared web user.
#[must_use]
pub fn web_server_user() -> &'static str {
    "www-data"
}

/// The group the web server's user belongs to.
///
/// Separate from the user because the two are only accidentally the same word:
/// what an account's home is group-owned by is a GROUP, and naming the user
/// there would be right by coincidence on both families and wrong the first
/// time one of them changed. Debian's nginx package creates `www-data` as both.
#[must_use]
pub fn web_server_group() -> &'static str {
    "www-data"
}

/// Name of the php-fpm systemd service unit for `version`, e.g. `php8.3-fpm`.
///
/// Sury names the unit after the package it ships, so this must change in
/// lockstep with `debian_packages::php_package`.
#[must_use]
pub fn php_fpm_service(version: &str) -> String {
    format!("php{version}-fpm")
}

/// Absolute path of the php-fpm binary for `version`, e.g.
/// `/usr/sbin/php-fpm8.3`.
///
/// Sury installs a version-suffixed binary so several PHP versions coexist on
/// the same host.
#[must_use]
pub fn php_fpm_binary(version: &str) -> String {
    format!("/usr/sbin/php-fpm{version}")
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
///
/// The certificate operations read a certificate's public key and its expiry
/// with it, and generate the self-signed placeholder a site serves before a
/// real certificate arrives. Debian ships it at `/usr/bin/openssl`.
#[must_use]
pub fn openssl_binary() -> &'static str {
    "/usr/bin/openssl"
}
