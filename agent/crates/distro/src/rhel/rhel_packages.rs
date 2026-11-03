//! Package and binary names on the RHEL family, how its package manager
//! reports a failed install, and how it reports the updates it knows of.

use crate::package_failure::PackageFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
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

/// The PHP versions Remi publishes a php-fpm package for, oldest first.
///
/// Remi carries every release from 7.4 to 8.4 for the RHEL releases the panel
/// supports; the list is read by the installable-versions query and by the
/// install, so a version is offered exactly when it can be installed.
#[must_use]
pub fn installable_php_versions() -> &'static [&'static str] {
    &["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"]
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

/// The arguments of the cache-only check that lists pending updates
/// (issue #324).
///
/// `-C` is `--cacheonly`: dnf reads the metadata already on disk and never
/// contacts a repository, so the run makes no outbound call and refreshes
/// nothing. `-q` drops the "Last metadata expiration check" line. Without `-C`
/// this would become a refresh from a root process, which is why the arguments
/// are a constant no caller value reaches. No operand exists, so no `--` is
/// needed. A host whose cache was never built exits 1 with "Cache-only enabled
/// but no cache" (recorded in
/// `tests/fixtures/package_manager/dnf_check_update_no_cache.txt`), which needs
/// no separate probe.
pub const PENDING_UPDATES_ARGUMENTS: &[&str] = &["check-update", "-C", "-q"];

/// The status `dnf check-update` exits with when updates are available.
const DNF_UPDATES_AVAILABLE_STATUS: i32 = 100;

/// The heading dnf prints before the packages that REPLACE installed ones,
/// recorded in `tests/fixtures/package_manager/dnf_check_update_obsoleting.txt`.
const DNF_OBSOLETING_HEADING: &str = "Obsoleting Packages";

/// Reads the output of [`PENDING_UPDATES_ARGUMENTS`]: its exit `status` and its
/// `stdout`.
///
/// `dnf check-update` exits 0 when nothing is available, 100 when something is,
/// and anything else — 1 included — for an error, so 0 and 100 are the only
/// successes. Each row is `NAME.ARCH VERSION REPOSITORY`, separated by runs of
/// spaces (a long name widens the column, it does not wrap; recorded in
/// `dnf_check_update_long_and_plus_names.txt`). A blank line is ignored. Rows
/// under the `Obsoleting Packages` heading are the packages that replace an
/// installed one: they are reported like any other row, and the indented line
/// beneath each, which names the package being replaced, is read and dropped.
/// Anything else fails the read, so a line this grammar has never seen is a
/// failure to read and never a skipped update.
///
/// dnf prints only the new version, so `installed_version` is `None` on every
/// row; the architecture suffix is dropped from the name.
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::Unsuccessful`] for a status other than 0 or
/// 100; [`PendingUpdatesFailure::UnrecognisedLine`] for the first line outside
/// the grammar, or for any output at all under status 0; and
/// [`PendingUpdatesFailure::IncompleteOutput`] when status 100 comes with no row.
pub fn read_pending_updates(
    status: i32,
    stdout: &str,
) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
    match status {
        0 => match stdout
            .lines()
            .enumerate()
            .find(|(_, line)| !line.trim().is_empty())
        {
            None => Ok(Vec::new()),
            Some((index, _)) => Err(PendingUpdatesFailure::UnrecognisedLine { line: index + 1 }),
        },
        DNF_UPDATES_AVAILABLE_STATUS => {
            let updates = read_rows(stdout)?;
            if updates.is_empty() {
                Err(PendingUpdatesFailure::IncompleteOutput)
            } else {
                Ok(updates)
            }
        }
        status => Err(PendingUpdatesFailure::Unsuccessful { status }),
    }
}

/// Reads the rows of a `check-update` answer that said updates exist.
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::UnrecognisedLine`] for the first line that is
/// not a row, the obsoleting heading, a blank line, or an indented replaced
/// package under that heading.
fn read_rows(stdout: &str) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
    let mut updates = Vec::new();
    let mut obsoleting = false;
    for (index, line) in stdout.lines().enumerate() {
        let unrecognised = PendingUpdatesFailure::UnrecognisedLine { line: index + 1 };
        if line.trim().is_empty() {
            continue;
        }
        if line == DNF_OBSOLETING_HEADING {
            obsoleting = true;
            continue;
        }

        let update = parse_row(line).ok_or(unrecognised)?;
        // The indented line under an obsoleting row names the package that is
        // replaced, not another update.
        if line.starts_with(char::is_whitespace) {
            if !obsoleting {
                return Err(unrecognised);
            }
            continue;
        }
        updates.push(update);
    }
    Ok(updates)
}

/// Reads one `NAME.ARCH VERSION REPOSITORY` row, or `None` when the line is not
/// exactly three printable tokens with an architecture after the last dot.
fn parse_row(line: &str) -> Option<PendingUpdate> {
    let mut fields = line.split_whitespace();
    let (name_and_arch, version, repository) = (fields.next()?, fields.next()?, fields.next()?);
    if fields.next().is_some() {
        return None;
    }

    let (name, arch) = name_and_arch.rsplit_once('.')?;
    if name.is_empty()
        || arch.is_empty()
        || !arch
            .bytes()
            .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'_')
    {
        return None;
    }
    if ![name, version, repository].into_iter().all(is_token) {
        return None;
    }

    Some(PendingUpdate {
        package_name: name.to_owned(),
        available_version: version.to_owned(),
        installed_version: None,
        source: repository.to_owned(),
    })
}

/// Whether `text` is a non-empty run of printable, non-space ASCII.
fn is_token(text: &str) -> bool {
    !text.is_empty() && text.bytes().all(|byte| byte.is_ascii_graphic())
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_packages_tests.rs"]
mod tests;
