//! Package and binary names on the Debian family, and how its package manager
//! reports a failed install.

use crate::package_failure::PackageFailure;
use crate::starts_with_http_status::starts_with_http_status;

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

/// The status `apt-get` exits with when an operation failed.
///
/// `apt-get` documents 100 for every error and 0 otherwise; a run that ended
/// on any other status (a signal, a status from the service manager) did not
/// reach apt's own error reporting, so its output is not read as apt's.
const APT_FAILURE_STATUS: i32 = 100;

/// What `apt-get` prefixes a fetch it gave up on with.
///
/// The URL follows, then two spaces, then the reason — an HTTP status line
/// (`404  Not Found`) when the source answered, the resolver's or the
/// connection's complaint when it did not. Recorded in
/// `tests/fixtures/package_manager/apt_source_{refused,unreachable}.txt`.
const APT_FETCH_FAILURE: &str = "E: Failed to fetch ";

/// The reasons `apt-get` gives for a fetch that reached no source at all.
///
/// Its HTTP method's own wording: an unresolvable name (`Temporary failure
/// resolving`, `Could not resolve`) and a connection that never completed
/// (`Could not connect to`, `Unable to connect to`, `Connection timed out`,
/// `Network is unreachable`).
const APT_UNREACHABLE_REASONS: [&str; 6] = [
    "Temporary failure resolving",
    "Could not resolve",
    "Could not connect to",
    "Unable to connect to",
    "Connection timed out",
    "Network is unreachable",
];

/// Reads a failed `apt-get install` run: its exit `status` and its combined
/// `output`.
///
/// Only the `E: Failed to fetch` lines are read, because they are the lines
/// apt writes when a source let it down and nothing else writes them. A reason
/// that opens with a three-digit HTTP status is a source that answered and
/// refused; one naming a resolver or connection failure is a source nobody
/// reached. A refusal anywhere in the run outweighs unreachable sources
/// elsewhere in it (see [`PackageFailure::SourceRefused`]). Anything else —
/// `Unable to locate package`, a dpkg failure, a held lock — is
/// [`PackageFailure::Local`].
#[must_use]
pub fn classify_package_failure(status: i32, output: &str) -> PackageFailure {
    if status != APT_FAILURE_STATUS {
        return PackageFailure::Local;
    }

    let mut verdict = PackageFailure::Local;
    for reason in output
        .lines()
        .filter_map(|line| line.strip_prefix(APT_FETCH_FAILURE))
        .filter_map(|rest| rest.split_once("  ").map(|(_, reason)| reason.trim_start()))
    {
        if starts_with_http_status(reason) {
            return PackageFailure::SourceRefused;
        }
        if APT_UNREACHABLE_REASONS
            .iter()
            .any(|marker| reason.starts_with(marker))
        {
            verdict = PackageFailure::SourceUnreachable;
        }
    }
    verdict
}

#[cfg(test)]
#[path = "../tests/debian/debian_packages_tests.rs"]
mod tests;
