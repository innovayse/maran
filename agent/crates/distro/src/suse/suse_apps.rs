//! The app store's package names and Apache preparation on the SUSE family.

use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;

/// The RPM leaves `apache2` stopped and disabled after the install.
/// UNVERIFIED: confirm with `zypper install apache2; systemctl is-active apache2` in the pinned
/// Leap image.
#[must_use]
pub fn apache_install_starts_service() -> bool {
    false
}

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

/// The multi-processing modules: `apache2` needs one of them and the dry run of its removal
/// lists the installed one (observed in `opensuse/leap:latest` 16.0, where installing
/// `apache2` brings `apache2-prefork`, and `zypper remove --dry-run apache2` removes both).
#[must_use]
pub fn apache_companion_packages() -> &'static [&'static str] {
    &["apache2-prefork", "apache2-worker", "apache2-event"]
}

/// A file in `conf.d`, which the packaged configuration includes.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) after installing `apache2`: `httpd.conf` includes
/// `default-server.conf` (line 210), and `grep -n IncludeOptional /etc/apache2/default-server.conf`
/// shows `IncludeOptional /etc/apache2/conf.d/*.conf`. `httpd.conf` itself includes only
/// `vhosts.d/*.conf` optionally. A `conf.d/maran.conf` holding a bad directive made `httpd -t` fail
/// on that file's line, so the directory is read.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/apache2/conf.d/maran.conf"
}

/// Modules the panel's vhost templates use; SUSE loads them from `APACHE_MODULES` until
/// `a2enmod` adds them.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0): `a2enmod -l` right after installing `apache2`
/// lists `alias auth_basic authn_file authz_core authz_host authz_user dir expires mime ssl` and
/// not `headers proxy proxy_fcgi proxy_http rewrite`; running `a2enmod` on each of the fifteen
/// names exits 0 (a present module answers `already present`), and afterwards `a2enmod -l` lists
/// all of them and `httpd -t` is `Syntax OK`.
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

/// The module enabler of the `apache2` package: `/usr/sbin/a2enmod`, observed with
/// `rpm -ql apache2 | grep a2enmod` in `opensuse/leap:latest` (Leap 16.0).
#[must_use]
pub fn apache_module_enabler() -> Option<AdapterBinary> {
    Some(AdapterBinary::fixed("/usr/sbin/a2enmod"))
}

/// The per-major package: `nodejs22`.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) with `zypper search -s nodejs`: the repositories
/// carry `nodejs22` and `nodejs24` (with `npm22`/`npm24` as separate packages) and NO `nodejs18`
/// or `nodejs20`, so on this release only majors 22 and 24 are offered; the availability query
/// finds the others absent and they are not listed. `nodejs-default` and `nodejs-common` are
/// not majors. `/usr/bin/node` is installed by `nodejs-common`, a dispatcher that runs the
/// highest installed major; see `node_binary`.
#[must_use]
pub fn nodejs_package(major: &str) -> String {
    format!("nodejs{major}")
}

/// The Node.js interpreter of `major`: the per-major package installs `node<major>` beside the
/// other majors' and leaves the unversioned `node` to `nodejs-common`.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0): `rpm -ql nodejs22 | grep bin/` is
/// `/usr/bin/node22` (and `nodejs24` ships `/usr/bin/node24`); both coexist, `node22 --version`
/// prints v22.23.4, and the unversioned `/usr/bin/node` (package `nodejs-common`, a dispatcher
/// with no alternatives entry) ran the highest installed major (v24 with both installed), so the
/// versioned path is the only one that names the asked-for major.
#[must_use]
pub fn node_binary(major: &NodeMajor) -> AdapterBinary {
    AdapterBinary::versioned(format!("/usr/bin/node{}", major.as_str()))
}
