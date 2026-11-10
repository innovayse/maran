//! The app store's package names and Apache preparation on the SUSE family.

use crate::adapter_binary::AdapterBinary;

/// The package that provides nginx.
#[must_use]
pub fn nginx_package() -> &'static str {
    "nginx"
}

/// The package that provides Apache.
#[must_use]
pub fn apache_package() -> &'static str {
    "apache2"
}

/// TLS is part of `apache2` on this family.
#[must_use]
pub fn apache_extra_packages() -> &'static [&'static str] {
    &[]
}

/// A file in `conf.d`, which the packaged configuration includes.
/// UNVERIFIED: confirm with `grep -r 'conf.d' /etc/apache2/httpd.conf` in the pinned Leap image.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/apache2/conf.d/maran.conf"
}

/// Modules the panel's vhost templates use; SUSE loads them from `APACHE_MODULES` until
/// `a2enmod` adds them.
/// UNVERIFIED: confirm with `a2enmod -l` in the pinned Leap image.
#[must_use]
pub fn apache_required_modules() -> &'static [&'static str] {
    &[
        "alias",
        "authn_file",
        "auth_basic",
        "authz_core",
        "authz_host",
        "authz_user",
        "dir",
        "expires",
        "headers",
        "mime",
        "proxy",
        "proxy_fcgi",
        "proxy_http",
        "rewrite",
        "ssl",
    ]
}

/// The module enabler of the `apache2` package.
#[must_use]
pub fn apache_module_enabler() -> Option<AdapterBinary> {
    Some(AdapterBinary::fixed("/usr/sbin/a2enmod"))
}

/// The per-major package: `nodejs20`.
/// UNVERIFIED: confirm with `zypper search nodejs` in the pinned Leap image.
#[must_use]
pub fn nodejs_package(major: &str) -> String {
    format!("nodejs{major}")
}

/// The Node.js interpreter.
#[must_use]
pub fn node_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/node")
}
