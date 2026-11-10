//! The app store's package names and Apache preparation on the RHEL family.

use crate::adapter_binary::AdapterBinary;

/// The package that provides nginx.
#[must_use]
pub fn nginx_package() -> &'static str {
    "nginx"
}

/// The package that provides Apache.
#[must_use]
pub fn apache_package() -> &'static str {
    "httpd"
}

/// TLS is a separate package on the RHEL family.
#[must_use]
pub fn apache_extra_packages() -> &'static [&'static str] {
    &["mod_ssl"]
}

/// A file in `conf.d`, which the packaged `httpd.conf` includes.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/httpd/conf.d/maran.conf"
}

/// `httpd` loads the proxy, rewrite, headers and expires modules from `conf.modules.d` already,
/// and has no module enabler.
#[must_use]
pub fn apache_required_modules() -> &'static [&'static str] {
    &[]
}

/// No module enabler on this family.
#[must_use]
pub fn apache_module_enabler() -> Option<AdapterBinary> {
    None
}

/// The per-major package: `nodejs20`.
/// UNVERIFIED: read from the distribution documentation, confirm with
/// `dnf -C list --available 'nodejs*'` on AlmaLinux 9; a major the sources do not carry is
/// simply not offered.
#[must_use]
pub fn nodejs_package(major: &str) -> String {
    format!("nodejs{major}")
}

/// The Node.js interpreter.
#[must_use]
pub fn node_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/node")
}
