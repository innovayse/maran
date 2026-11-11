//! The app store's package names and Apache preparation on the Debian family.

use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;

/// The package that provides nginx.
#[must_use]
pub fn nginx_package() -> &'static str {
    "nginx"
}

/// The package that provides Apache. Its control script, unit and identities are in
/// [`crate::debian::debian_services`].
#[must_use]
pub fn apache_package() -> &'static str {
    "apache2"
}

/// Debian's `apache2` package starts the service in its `postinst`: the install itself binds the
/// ports, which fails while nginx holds port 80 and leaves the package unconfigured.
#[must_use]
pub fn apache_install_starts_service() -> bool {
    true
}

/// Debian's `apache2` package carries TLS, so no further package.
#[must_use]
pub fn apache_extra_packages() -> &'static [&'static str] {
    &[]
}

/// Debian's `apache2` package brings no companion package that goes with it on removal.
#[must_use]
pub fn apache_companion_packages() -> &'static [&'static str] {
    &[]
}

/// A file in `conf-enabled`, which the packaged configuration includes.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/apache2/conf-enabled/maran.conf"
}

/// Modules the panel's vhost templates use; Debian ships them disabled until `a2enmod`.
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

/// The package behind every Node.js major: Debian's sources carry one `nodejs` package of the
/// release's own major.
#[must_use]
pub fn nodejs_package() -> &'static str {
    "nodejs"
}

/// The Node.js interpreter. The one `nodejs` package carries one interpreter, so `_major` does
/// not change the answer: a second major cannot be installed beside it.
#[must_use]
pub fn node_binary(_major: &NodeMajor) -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/node")
}

/// The major of the `nodejs` candidate in `apt-cache policy` output.
///
/// Reads the `Candidate:` line of the `nodejs:` stanza; the version may carry an epoch
/// (`2:18.19.1`) which is dropped, and the major is the digits before the first dot. `None`
/// for a stanza without a candidate (`(none)`) and for output that has no such stanza.
#[must_use]
pub fn nodejs_candidate_major(policy_stdout: &str) -> Option<String> {
    let mut in_stanza = false;
    for line in policy_stdout.lines() {
        if !line.starts_with(char::is_whitespace) && !line.trim().is_empty() {
            in_stanza = line.trim_end() == "nodejs:";
            continue;
        }
        if !in_stanza {
            continue;
        }
        let Some(version) = line.trim().strip_prefix("Candidate: ") else {
            continue;
        };
        let version = version.split_once(':').map_or(version, |(_, rest)| rest);
        let major: String = version.chars().take_while(char::is_ascii_digit).collect();
        return (!major.is_empty()).then_some(major);
    }
    None
}

#[cfg(test)]
#[path = "../tests/debian/debian_apps_tests.rs"]
mod tests;
