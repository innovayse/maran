//! The dry run of a package removal on the RHEL family.

use crate::removal_facts::RemovalFacts;
use crate::removal_simulation::RemovalSimulation;
use crate::removal_simulation_failure::RemovalSimulationFailure;
use crate::rhel::RhelAdapter;

/// The section header of the requested package.
const REMOVING: &str = "Removing:";

/// The section header of the packages that need the requested one.
const REMOVING_DEPENDENTS: &str = "Removing dependent packages:";

/// The section header of the dependencies nothing needs once the removal is done.
const REMOVING_UNUSED: &str = "Removing unused dependencies:";

/// The line that opens the summary, printed once the plan is resolved and only then.
const SUMMARY: &str = "Transaction Summary";

/// The first line of the resolved plan.
const RESOLVED: &str = "Dependencies resolved.";

/// What the table's column header line starts with.
const TABLE_HEADER: &str = " Package";

/// Lines the package manager prints around the plan that carry no package: the age of its
/// metadata cache and the subscription refresh of a registered host.
const NOISE_PREFIXES: [&str; 2] = [
    "Last metadata expiration check:",
    "Updating Subscription Management repositories.",
];

/// What the summary's count line starts with (`Remove  5 Packages`).
const SUMMARY_COUNT: &str = "Remove ";

/// What the summary's space line starts with.
const FREED_SPACE: &str = "Freed space:";

/// What the dry run ends with after answering no.
const ABORTED: &str = "Operation aborted.";

/// The arguments of the dry run: `--assumeno` answers the confirmation with no, so the plan is
/// printed and nothing is removed.
#[must_use]
pub fn removal_simulation_arguments(package: &str) -> Vec<String> {
    ["remove", "--assumeno", "--", package]
        .iter()
        .map(|argument| (*argument).to_owned())
        .collect()
}

/// Which part of the output the reader is in.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Section {
    /// Before the table, between sections, or after the summary: no row is expected.
    Outside,
    /// The requested package and the packages that need it.
    Removed,
    /// Dependencies nothing needs any more.
    Unused,
}

/// Reads `dnf remove --assumeno`: the transaction table, whose sections `Removing:` and
/// `Removing dependent packages:` hold the packages removed because the requested one is, and
/// `Removing unused dependencies:` the ones removed because nothing needs them any more.
///
/// A row starts with one space and the package name; a row whose columns do not fit holds the
/// name alone and the columns on the next, indented line. The dry run exits 1 with
/// `Operation aborted.` after printing the plan, so 0 and 1 are both a plan when the summary
/// line is there; the summary is what proves the plan was resolved.
///
/// The reader fails closed: every line must be one it knows (the table, its header, the summary
/// lines, the noise around them) and anything else, a section it does not collect included,
/// makes the whole read `Unrecognised`, so a layout it does not understand can never read as a
/// smaller plan.
///
/// Recorded: `dnf remove --assumeno` on `almalinux:9` (dnf 4.14.0), the fixtures
/// `dnf_remove_simulation_*.txt` and `dnf_remove_not_installed.txt`. UNVERIFIED: dnf 5 (the
/// Fedora generation) prints another layout and is refused by this reader.
///
/// # Errors
///
/// Returns [`RemovalSimulationFailure::Unsuccessful`] for a status other than 0 or 1 and
/// [`RemovalSimulationFailure::Unrecognised`] for output with no summary, no removed package, or
/// any line the reader does not know.
pub fn read_removal_simulation(
    status: i32,
    stdout: &str,
) -> Result<RemovalSimulation, RemovalSimulationFailure> {
    if !(0..=1).contains(&status) {
        return Err(RemovalSimulationFailure::Unsuccessful { status });
    }

    let mut removed = Vec::new();
    let mut unused = Vec::new();
    let mut section = Section::Outside;
    let mut summarised = false;
    let mut awaiting_columns = false;
    for line in stdout.lines() {
        let trimmed = line.trim_end();
        if awaiting_columns {
            // A name alone on its line promised the columns on the next one.
            if !is_columns(trimmed) {
                return Err(RemovalSimulationFailure::Unrecognised);
            }
            awaiting_columns = false;
            continue;
        }

        if trimmed.is_empty() {
            section = Section::Outside;
        } else if trimmed.starts_with(SUMMARY) {
            summarised = true;
            section = Section::Outside;
        } else if trimmed == REMOVING || trimmed == REMOVING_DEPENDENTS {
            section = Section::Removed;
        } else if trimmed == REMOVING_UNUSED {
            section = Section::Unused;
        } else if section != Section::Outside {
            let (name, columns_follow) =
                row(trimmed).ok_or(RemovalSimulationFailure::Unrecognised)?;
            awaiting_columns = columns_follow;
            if section == Section::Removed {
                removed.push(name.to_owned());
            } else {
                unused.push(name.to_owned());
            }
        } else if !is_known_outside_the_table(trimmed, summarised) {
            return Err(RemovalSimulationFailure::Unrecognised);
        }
    }

    if !summarised || awaiting_columns || removed.is_empty() {
        return Err(RemovalSimulationFailure::Unrecognised);
    }
    Ok(RemovalSimulation { removed, unused })
}

/// Whether `line` is one the dry run prints outside the rows: the resolved marker, the rulers,
/// the column header, the noise, and, after the summary, its three lines.
fn is_known_outside_the_table(line: &str, summarised: bool) -> bool {
    if summarised {
        return line.starts_with(SUMMARY_COUNT)
            || line.starts_with(FREED_SPACE)
            || line == ABORTED
            || is_ruler(line);
    }
    line == RESOLVED
        || is_ruler(line)
        || line.starts_with(TABLE_HEADER)
        || NOISE_PREFIXES.iter().any(|prefix| line.starts_with(prefix))
}

/// A line of `=` only: the table's rulers.
fn is_ruler(line: &str) -> bool {
    line.chars().all(|character| character == '=')
}

/// A table row: the package name of a line that starts with exactly one space, and whether the
/// columns follow on the next line (the name is alone). A row with columns has at least four
/// (architecture, version, repository, size and unit make five words; four is the floor so a
/// size without a unit is still a row).
fn row(line: &str) -> Option<(&str, bool)> {
    let rest = line.strip_prefix(' ')?;
    if rest.starts_with(' ') {
        return None;
    }
    let mut words = rest.split_whitespace();
    let name = words.next().filter(|name| is_package_name(name))?;
    match words.count() {
        0 => Some((name, true)),
        columns if columns >= 4 => Some((name, false)),
        _ => None,
    }
}

/// The indented columns line a wrapped row continues on.
fn is_columns(line: &str) -> bool {
    line.starts_with("  ") && line.split_whitespace().count() >= 4
}

/// Whether `name` is spelled like a package name: ASCII letters and digits, `.`, `_`, `+`, `-`.
fn is_package_name(name: &str) -> bool {
    !name.is_empty()
        && name.chars().all(|character| {
            character.is_ascii_alphanumeric() || matches!(character, '.' | '_' | '+' | '-')
        })
}

impl RemovalFacts for RhelAdapter {
    fn removal_simulation_arguments(&self, package: &str) -> Vec<String> {
        removal_simulation_arguments(package)
    }

    fn read_removal_simulation(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<RemovalSimulation, RemovalSimulationFailure> {
        read_removal_simulation(status, stdout)
    }
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_removal_tests.rs"]
mod tests;
