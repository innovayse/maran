//! How the Debian family is asked which packages its sources offer, and how the
//! answer is read.

use crate::adapter_binary::AdapterBinary;
use crate::package_query_failure::PackageQueryFailure;

/// What opens the line that names the version `apt-cache policy` would install.
const CANDIDATE_PREFIX: &str = "Candidate: ";

/// What `apt-cache policy` prints as the candidate of a package it knows by
/// name and no source carries (a name some other package merely mentions).
/// Recorded in `tests/fixtures/package_manager/apt_policy_ubuntu_php83_only.txt`.
const NO_CANDIDATE: &str = "(none)";

/// Absolute path of the program that reads the package cache's candidates.
///
/// `apt-cache` at `/usr/bin/apt-cache`. It reads the lists already on disk and
/// opens no connection, so asking it changes nothing and reaches nothing.
#[must_use]
pub fn package_candidate_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/apt-cache")
}

/// The arguments of the run that reports the candidate of each of `packages`.
///
/// `policy` is read-only. `--` ends option parsing before the operands (a name
/// is an adapter-derived package spelling, so none starts with `-`, and the
/// separator is the second line of defence, measured to be accepted).
#[must_use]
pub fn package_candidate_arguments(packages: &[String]) -> Vec<String> {
    let mut arguments = vec!["policy".to_owned(), "--".to_owned()];
    arguments.extend(packages.iter().cloned());
    arguments
}

/// Reads the output of [`package_candidate_arguments`]: its exit `status` and
/// `stdout`, and which of `packages` have a candidate version.
///
/// `apt-cache policy` prints nothing for a name it has never heard of (exit 0),
/// and a header with `Candidate: (none)` for one it knows and no source carries;
/// both mean "not offered". The same empty output also comes from a lists
/// directory that was never built, which this reading cannot tell apart, so the
/// caller asks the catalogue probe first. The answer lists the offered packages
/// in the order `packages` gave them, once each.
///
/// # Errors
///
/// Returns [`PackageQueryFailure::Unsuccessful`] for a status other than 0 and
/// [`PackageQueryFailure::UnrecognisedLine`] for a line that is neither a package
/// header nor an indented detail line.
pub fn read_package_candidates(
    status: i32,
    stdout: &str,
    packages: &[String],
) -> Result<Vec<String>, PackageQueryFailure> {
    if status != 0 {
        return Err(PackageQueryFailure::Unsuccessful { status });
    }

    let mut offered: Vec<&str> = Vec::new();
    let mut current: Option<&str> = None;
    for (index, line) in stdout.lines().enumerate() {
        if line.trim().is_empty() {
            continue;
        }
        if line.starts_with(char::is_whitespace) {
            let candidate = line.trim_start().strip_prefix(CANDIDATE_PREFIX);
            if let (Some(name), Some(version)) = (current, candidate)
                && version != NO_CANDIDATE
            {
                offered.push(name);
            }
        } else if let Some(name) = line.strip_suffix(':') {
            current = Some(name);
        } else {
            return Err(PackageQueryFailure::UnrecognisedLine { line: index + 1 });
        }
    }

    // Only names that were asked about count, in the order they were asked.
    Ok(packages
        .iter()
        .filter(|package| offered.contains(&package.as_str()))
        .cloned()
        .collect())
}

#[cfg(test)]
#[path = "../tests/debian/debian_package_candidates_tests.rs"]
mod tests;
