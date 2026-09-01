//! Service and binary names on the RHEL family.

/// Absolute path of the nginx binary, for the process-execution allow-list.
#[must_use]
pub fn nginx_binary() -> &'static str {
    "/usr/sbin/nginx"
}

/// Name of the nginx systemd service unit.
///
/// RHEL's nginx package registers the service under this name; the Debian
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
/// RHEL's nginx package creates and runs as `nginx`, not `www-data`.
#[must_use]
pub fn web_server_user() -> &'static str {
    "nginx"
}

/// The group the web server's user belongs to.
///
/// Separate from the user because the two are only accidentally the same word:
/// what an account's home is group-owned by is a GROUP, and naming the user
/// there would be right by coincidence on both families and wrong the first
/// time one of them changed. RHEL's nginx package creates `nginx` as both.
#[must_use]
pub fn web_server_group() -> &'static str {
    "nginx"
}

/// Name of the php-fpm systemd service unit for `version`, e.g.
/// `php83-php-fpm`.
///
/// Remi names the unit after the package it ships, so this must change in
/// lockstep with `rhel_packages::php_package`.
#[must_use]
pub fn php_fpm_service(version: &str) -> String {
    format!("php{}-php-fpm", version.replace('.', ""))
}

/// Absolute path of the php-fpm binary for `version`, e.g.
/// `/opt/remi/php83/root/usr/sbin/php-fpm`.
///
/// Remi ships each version as a software collection rooted at
/// `/opt/remi/php<version>/root`, so the binary keeps its plain name and the
/// VERSION is in the directory — the mirror image of the Debian family, where
/// the name carries the version and the directory does not. It is not
/// `/usr/sbin/php-fpm83`: no such file exists on a Remi host, and a pool write
/// pointed at it fails to spawn its own validator, so every PHP pool on the
/// whole family would fail. The polygon asserts this path exists
/// (`crates/agent/tests/php_pools_on_a_real_host.rs`), because a path that is
/// merely plausible is how this was wrong in the first place.
#[must_use]
pub fn php_fpm_binary(version: &str) -> String {
    format!(
        "/opt/remi/php{version}/root/usr/sbin/php-fpm",
        version = version.replace('.', "")
    )
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
///
/// The same path as on the Debian family today, and asked of the adapter all
/// the same: a binary is spawnable because this trait named it, not because
/// the two families happen to agree.
#[must_use]
pub fn openssl_binary() -> &'static str {
    "/usr/bin/openssl"
}
