//! How the SUSE family is asked which packages its repositories offer, and how the answer is read.
//!
//! Nothing in this file was measured by us: the arguments and the table layout are read from
//! `man zypper` and are UNVERIFIED until the first run on the pinned openSUSE Leap image, and no
//! recording of that run exists yet (`tests/fixtures/package_manager/` holds none for this family).

use crate::adapter_binary::AdapterBinary;
use crate::is_printable_token::is_printable_token;
use crate::package_query_failure::PackageQueryFailure;
use crate::suse::suse_packages;

/// The status `zypper search` exits with when it found rows.
const ZYPPER_OK_STATUS: i32 = 0;

/// The status `zypper search` exits with when nothing matched
/// (`ZYPPER_EXIT_INF_CAP_NOT_FOUND`), with `No matching items found.` on standard output.
const ZYPPER_NOT_FOUND_STATUS: i32 = 104;

/// The sentence `zypper search` prints when nothing matched.
const NO_MATCH_LINE: &str = "No matching items found.";

/// The kind of item the search is restricted to, and so the only value of the table's `Type`
/// column.
const PACKAGE_TYPE: &str = "package";

/// The header of the `search --details` table, one cell per column.
const SEARCH_HEADER: [&str; 6] = ["S", "Name", "Type", "Version", "Arch", "Repository"];

/// The fixed arguments before the operands.
///
/// `--non-interactive` answers no prompt and `--no-refresh` makes zypper read the metadata already
/// on disk and contact no repository. `search --details --match-exact -t package` lists, one row
/// per version and repository, the packages whose name is exactly an operand. `--` ends option
/// parsing before the operands. UNVERIFIED: confirm with
/// `zypper --non-interactive --no-refresh search --details --match-exact -t package -- php8-fpm`
/// in the pinned Leap image, including that `--` is honoured.
const SEARCH_ARGUMENTS: [&str; 8] = [
    "--non-interactive",
    "--no-refresh",
    "search",
    "--details",
    "--match-exact",
    "-t",
    "package",
    "--",
];

/// Absolute path of the program that lists the packages the repositories carry.
///
/// `zypper` at `/usr/bin/zypper`, the file [`crate::PackageFacts::package_manager`] names as plain
/// text for the install unit. UNVERIFIED: confirm with `command -v zypper` in the pinned image.
#[must_use]
pub fn package_candidate_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/zypper")
}

/// The arguments of the run that lists which of `packages` are available.
///
/// A cache-only, read-only search for each name exactly (see `SEARCH_ARGUMENTS`), with the operands
/// after `--`.
#[must_use]
pub fn package_candidate_arguments(packages: &[String]) -> Vec<String> {
    let mut arguments: Vec<String> = SEARCH_ARGUMENTS
        .iter()
        .map(|argument| (*argument).to_owned())
        .collect();
    arguments.extend(packages.iter().cloned());
    arguments
}

/// Reads the output of [`package_candidate_arguments`]: its exit `status`, `stdout` and `stderr`,
/// and which of `packages` are available.
///
/// Exit 0 prints two progress sentences, the header
/// `S | Name | Type | Version | Arch | Repository`, a rule of `-` and `+`, and one row per match
/// (a package offered by two repositories has two rows); names that matched nothing are simply
/// absent. Exit 104 with `No matching items found.`
/// and no row means none of the names matched, which is an answer. Anything else is a failure of
/// the query and is never read as "nothing is offered": a skipped repository (106), a failed
/// refresh (4), a lock (7), and also exit 0 with no row, or exit 104 beside a row, which are
/// outputs this grammar cannot reconcile with the status. Every row must be six cells with a
/// printable `Name`, `Type` equal to `package`, printable `Version` and `Arch`, a status of at most
/// two printable characters and a printable repository label; any other shape fails the whole read
/// with its line number. The answer lists the offered packages in the order `packages` gave them,
/// once each. Standard error is not read: a warning there changes no verdict, and no byte of it
/// can reach one.
///
/// UNVERIFIED: the layout is read from `man zypper`; the recording on a real Leap container is
/// owed.
///
/// # Errors
///
/// Returns [`PackageQueryFailure::Unsuccessful`] for any status other than 0 or 104, for status 0
/// with no row and for status 104 with a row, and [`PackageQueryFailure::UnrecognisedLine`] for the
/// first line outside the grammar (a row before the header, a second header, a rule before the
/// header, a row of another shape).
pub fn read_package_candidates(
    status: i32,
    stdout: &str,
    _stderr: &str,
    packages: &[String],
) -> Result<Vec<String>, PackageQueryFailure> {
    if status != ZYPPER_OK_STATUS && status != ZYPPER_NOT_FOUND_STATUS {
        return Err(PackageQueryFailure::Unsuccessful { status });
    }

    let mut offered: Vec<&str> = Vec::new();
    let mut header_seen = false;
    for (index, line) in stdout.lines().enumerate() {
        let unrecognised = PackageQueryFailure::UnrecognisedLine { line: index + 1 };
        let text = line.trim();
        let is_progress = suse_packages::PREAMBLE_LINES.contains(&text);
        if text.is_empty() || is_progress || text == NO_MATCH_LINE {
            continue;
        }
        if suse_packages::is_table_rule(text) {
            if header_seen {
                continue;
            }
            return Err(unrecognised);
        }

        let cells = suse_packages::table_cells(text);
        if cells == SEARCH_HEADER {
            if header_seen {
                return Err(unrecognised);
            }
            header_seen = true;
        } else if header_seen {
            offered.push(offered_name(&cells).ok_or(unrecognised)?);
        } else {
            return Err(unrecognised);
        }
    }

    // Status 0 promises rows and status 104 promises none; an output that says otherwise is not
    // an answer.
    let has_rows = !offered.is_empty();
    if has_rows != (status == ZYPPER_OK_STATUS) {
        return Err(PackageQueryFailure::Unsuccessful { status });
    }

    Ok(packages
        .iter()
        .filter(|package| offered.contains(&package.as_str()))
        .cloned()
        .collect())
}

/// Reads the six cells of one search row, or `None` when the row is not exactly six well-formed
/// cells; the package name otherwise.
fn offered_name<'a>(cells: &[&'a str]) -> Option<&'a str> {
    let &[marker, name, kind, version, architecture, repository] = cells else {
        return None;
    };
    let tokens_are_printable = [name, kind, version, architecture]
        .into_iter()
        .all(is_printable_token);
    if !tokens_are_printable || kind != PACKAGE_TYPE {
        return None;
    }

    let marker_is_valid = suse_packages::is_status_marker(marker);
    let repository_is_valid = suse_packages::is_repository_label(repository);
    (marker_is_valid && repository_is_valid).then_some(name)
}

#[cfg(test)]
#[path = "../tests/suse/suse_package_candidates_tests.rs"]
mod tests;
