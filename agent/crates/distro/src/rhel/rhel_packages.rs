//! Package and binary names on the RHEL family, and how its package manager
//! reports a failed install.

use crate::package_failure::PackageFailure;
use crate::starts_with_http_status::starts_with_http_status;

/// Name of the OS package that provides php-fpm for `version`, e.g.
/// `php83-php-fpm`.
///
/// Remi drops the dot from the version, matching the RPM naming convention
/// the rest of its PHP packages use, and correspondingly in
/// [`crate::rhel::rhel_paths::php_fpm_pool_directory`].
#[must_use]
pub fn php_package(version: &str) -> String {
    format!("php{}-php-fpm", version.replace('.', ""))
}

/// Absolute path of the package manager binary.
///
/// Remi's repository is installed and driven through `dnf`, the RHEL
/// family's package manager (spec §4).
#[must_use]
pub fn package_manager() -> &'static str {
    "/usr/bin/dnf"
}

/// The status `dnf` exits with when an operation failed.
///
/// `dnf` exits 1 for an error and 0 on success (and 100 only for
/// `check-update`, which is never run here); a run that ended on any other
/// status did not reach dnf's own error reporting, so its output is not read
/// as dnf's.
const DNF_FAILURE_STATUS: i32 = 1;

/// What `dnf` writes before the HTTP status of a source that answered with an
/// error, e.g. `Status code: 404 for http://…/repomd.xml`. Recorded in
/// `tests/fixtures/package_manager/dnf_source_refused.txt`.
const DNF_STATUS_CODE: &str = "Status code: ";

/// The libcurl failures `dnf` reports for a source it never reached, as it
/// quotes them: `Curl error (6)` could not resolve the host, `(5)` could not
/// resolve the proxy, `(7)` could not connect, `(28)` timed out. Recorded in
/// `tests/fixtures/package_manager/dnf_source_unreachable.txt` and
/// `dnf_source_connection_refused.txt`.
const DNF_UNREACHABLE_ERRORS: [&str; 4] = [
    "Curl error (5):",
    "Curl error (6):",
    "Curl error (7):",
    "Curl error (28):",
];

/// Reads a failed `dnf install` run: its exit `status` and its combined
/// `output`.
///
/// A `Status code: NNN` anywhere in the run is a source that answered and
/// refused, and outweighs unreachable sources elsewhere in it (see
/// [`PackageFailure::SourceRefused`]); a libcurl resolve, connect or timeout
/// error is a source nobody reached. Anything else — `No match for argument`,
/// a transaction check, a scriptlet failure, a held lock — is
/// [`PackageFailure::Local`].
#[must_use]
pub fn classify_package_failure(status: i32, output: &str) -> PackageFailure {
    if status != DNF_FAILURE_STATUS {
        return PackageFailure::Local;
    }

    let mut verdict = PackageFailure::Local;
    for line in output.lines() {
        let refused = line
            .split_once(DNF_STATUS_CODE)
            .is_some_and(|(_, after)| starts_with_http_status(after));
        if refused {
            return PackageFailure::SourceRefused;
        }
        if DNF_UNREACHABLE_ERRORS
            .iter()
            .any(|marker| line.contains(marker))
        {
            verdict = PackageFailure::SourceUnreachable;
        }
    }
    verdict
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_packages_tests.rs"]
mod tests;
