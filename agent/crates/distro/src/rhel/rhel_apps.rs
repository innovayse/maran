//! The app store's package names and Apache preparation on the RHEL family.

use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;

/// The RPM leaves `httpd` stopped and disabled after the install.
/// UNVERIFIED: confirm with `dnf install httpd; systemctl is-active httpd` on AlmaLinux 9.
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
    "httpd"
}

/// TLS is a separate package on the RHEL family.
#[must_use]
pub fn apache_extra_packages() -> &'static [&'static str] {
    &["mod_ssl"]
}

/// RHEL's `httpd` brings no companion package that goes with it on removal; what it pulls in as
/// plain dependencies is reported by the dry run as unused dependencies.
#[must_use]
pub fn apache_companion_packages() -> &'static [&'static str] {
    &[]
}

/// A file in `conf.d`, which the packaged `httpd.conf` includes.
///
/// Observed in `almalinux:9` (9.8) after `dnf install httpd mod_ssl`: `httpd.conf` line 358 is
/// `IncludeOptional conf.d/*.conf`, and the package runs as `User apache` / `Group apache`.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/httpd/conf.d/maran.conf"
}

/// `httpd` loads the proxy, rewrite, headers and expires modules from `conf.modules.d` already,
/// and has no module enabler.
///
/// Observed in `almalinux:9` (9.8) with `grep -h LoadModule /etc/httpd/conf.modules.d/*.conf
/// /etc/httpd/conf/httpd.conf`: `alias`, `auth_basic`, `authn_file`, `authz_core`, `authz_host`,
/// `authz_user`, `dir`, `expires`, `headers`, `mime`, `rewrite` (`httpd.conf` side) and `proxy`,
/// `proxy_fcgi`, `proxy_http`, `ssl` (`00-proxy.conf`, `00-ssl.conf`, the latter from `mod_ssl`)
/// are all loaded.
#[must_use]
pub fn apache_required_modules() -> &'static [&'static str] {
    &[]
}

/// No module enabler on this family.
#[must_use]
pub fn apache_module_enabler() -> Option<AdapterBinary> {
    None
}

/// The per-major package name the family's sources would need to carry: `nodejs20`.
///
/// Observed in `almalinux:9` (9.8) with `dnf module list nodejs` and `dnf list --available
/// 'nodejs*'`: NO such package exists. Node.js 18, 20, 22 and 24 are AppStream MODULE STREAMS
/// (`nodejs:20`, installed with `dnf module install nodejs:20/common`), whose packages are all
/// named `nodejs`/`npm`, and the only non-modular `nodejs` the repositories list is 16.20.2. The
/// availability query therefore never finds `nodejs20`, and no Node.js app is offered on this
/// family until the install can enable a stream (a module spec is not a package name the root
/// install unit takes). This is a gap, tracked as its own issue; the name below is kept so
/// the query answers "not offered" instead of offering a package that would install the wrong major.
#[must_use]
pub fn nodejs_package(major: &str) -> String {
    format!("nodejs{major}")
}

/// The Node.js interpreter, whichever `major` is asked about.
///
/// A module stream installs one interpreter at a time, at the unversioned path, and enabling
/// another stream replaces it. Observed in `almalinux:9` (9.8) with `dnf module install
/// nodejs:20/common` and `rpm -ql nodejs | grep bin/`: only `/usr/bin/node` (v20.20.2); there is
/// no `/usr/bin/node-20` and no per-major file to name. Whether the installed stream is the
/// asked-for `major` is read from `node --version` by the caller.
#[must_use]
pub fn node_binary(_major: &NodeMajor) -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/node")
}
