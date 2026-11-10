//! How the RHEL family is asked which packages its repositories offer, and how
//! the answer is read.

use crate::adapter_binary::AdapterBinary;
use crate::package_query_failure::PackageQueryFailure;

/// What `dnf list` exits with when nothing it was asked about matches.
const DNF_FAILURE_STATUS: i32 = 1;

/// The sentence `dnf` writes to standard error when none of the names matched.
/// Recorded in `tests/fixtures/package_manager/dnf_list_available_no_match.txt`.
const NO_MATCH: &str = "No matching Packages to list";

/// The heading `dnf` prints above the available packages.
const AVAILABLE_HEADING: &str = "Available Packages";

/// Absolute path of the program that lists the packages the repositories carry.
///
/// `dnf` at `/usr/bin/dnf`, the file [`crate::PackageFacts::package_manager`]
/// names as plain text for the install unit.
#[must_use]
pub fn package_candidate_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/dnf")
}

/// The arguments of the run that lists which of `packages` are available.
///
/// `-C` is `--cacheonly`: dnf reads the metadata already on disk and contacts no
/// repository, the same guard the pending-updates read has. `-q` drops the
/// metadata-expiry line. `--` ends option parsing before the operands.
#[must_use]
pub fn package_candidate_arguments(packages: &[String]) -> Vec<String> {
    let mut arguments: Vec<String> = ["-q", "-C", "list", "--available", "--"]
        .iter()
        .map(|argument| (*argument).to_owned())
        .collect();
    arguments.extend(packages.iter().cloned());
    arguments
}

/// Reads the output of [`package_candidate_arguments`]: its exit `status`,
/// `stdout` and `stderr`, and which of `packages` are available.
///
/// Exit 0 prints an `Available Packages` heading and one row per match, `NAME.ARCH
/// VERSION REPOSITORY`; names that matched nothing are simply absent. Exit 1 with
/// `No matching Packages to list` means none of the names matched, which is an
/// answer; exit 1 for any other reason (`Cache-only enabled but no cache`, a
/// repository that cannot be read) is a failure of the query and is never read
/// as "nothing is offered". The answer lists the offered packages in the order
/// `packages` gave them, once each.
///
/// # Errors
///
/// Returns [`PackageQueryFailure::Unsuccessful`] for any exit that is not the
/// two answers above and [`PackageQueryFailure::UnrecognisedLine`] for a row
/// with fewer than three columns.
pub fn read_package_candidates(
    status: i32,
    stdout: &str,
    stderr: &str,
    packages: &[String],
) -> Result<Vec<String>, PackageQueryFailure> {
    if status == DNF_FAILURE_STATUS && stderr.contains(NO_MATCH) {
        return Ok(Vec::new());
    }
    if status != 0 {
        return Err(PackageQueryFailure::Unsuccessful { status });
    }

    let mut offered: Vec<&str> = Vec::new();
    for (index, line) in stdout.lines().enumerate() {
        // A wrapped row continues on an indented line and names no package.
        if line.trim().is_empty()
            || line.trim() == AVAILABLE_HEADING
            || line.starts_with(char::is_whitespace)
        {
            continue;
        }
        let mut columns = line.split_whitespace();
        let name = columns
            .next()
            .and_then(|first| first.rsplit_once('.'))
            .map(|(name, _architecture)| name);
        match (name, columns.count()) {
            (Some(name), count) if count >= 2 => offered.push(name),
            _ => return Err(PackageQueryFailure::UnrecognisedLine { line: index + 1 }),
        }
    }

    Ok(packages
        .iter()
        .filter(|package| offered.contains(&package.as_str()))
        .cloned()
        .collect())
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_package_candidates_tests.rs"]
mod tests;
