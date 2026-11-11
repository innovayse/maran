//! The dry run of a package removal on the SUSE family.

use crate::removal_facts::RemovalFacts;
use crate::removal_simulation::RemovalSimulation;
use crate::removal_simulation_failure::RemovalSimulationFailure;
use crate::suse::SuseAdapter;

/// What a plan section about removals ends with: `The following package is going to be REMOVED:`.
const REMOVED_HEADER_END: &str = "REMOVED:";

/// What every section of the plan starts with.
const SECTION_START: &str = "The following ";

/// Lines the dry run prints around the plan that carry no package, by what they start with.
const KNOWN_LINE_PREFIXES: [&str; 7] = [
    "Reading installed packages...",
    "Resolving package dependencies...",
    "Loading repository data...",
    "Retrieving repository",
    "Package install size change:",
    "Backend:",
    "Continue?",
];

/// What the count line ends with: `2 packages to remove.`.
const COUNT_LINE_END: &str = "to remove.";

/// The arguments of the dry run: `--dry-run` prints the plan and changes nothing.
#[must_use]
pub fn removal_simulation_arguments(package: &str) -> Vec<String> {
    ["--non-interactive", "remove", "--dry-run", "--", package]
        .iter()
        .map(|argument| (*argument).to_owned())
        .collect()
}

/// Reads `zypper remove --dry-run`: the section `The following package(s) ... going to be
/// REMOVED:` followed by indented lines of package names, up to the next blank line.
///
/// `zypper remove` does not remove unused dependencies unless asked to, so the second set is
/// empty. The reader fails closed: a plan section about anything but removal (`... going to be
/// INSTALLED:`, `... is not supported by its vendor:`), a word under the removal header that is
/// not spelled like a package name, and any line outside the removal section that is not one of
/// the lines the dry run is known to print, make the read `Unrecognised`.
///
/// Recorded: `zypper --non-interactive remove --dry-run` on `opensuse/leap:latest` (Leap 16.0,
/// zypper 1.14.101), the fixtures `zypper_remove_dry_run_*.txt` and `zypper_remove_not_found.txt`.
/// Installing `apache2` there brings `apache2-prefork`, and removing `apache2` lists it too.
///
/// # Errors
///
/// Returns [`RemovalSimulationFailure::Unsuccessful`] for a status other than 0 and
/// [`RemovalSimulationFailure::Unrecognised`] for output with no removal section, with a
/// section of another kind, or with a line the reader does not know.
pub fn read_removal_simulation(
    status: i32,
    stdout: &str,
) -> Result<RemovalSimulation, RemovalSimulationFailure> {
    if status != 0 {
        return Err(RemovalSimulationFailure::Unsuccessful { status });
    }

    let mut removed = Vec::new();
    let mut in_removed = false;
    for line in stdout.lines() {
        let trimmed = line.trim_end();
        if trimmed.starts_with(SECTION_START) && trimmed.ends_with(':') {
            if !trimmed.ends_with(REMOVED_HEADER_END) {
                return Err(RemovalSimulationFailure::Unrecognised);
            }
            in_removed = true;
        } else if trimmed.is_empty() {
            in_removed = false;
        } else if in_removed {
            for word in trimmed.split_whitespace() {
                if !is_package_name(word) {
                    return Err(RemovalSimulationFailure::Unrecognised);
                }
                removed.push(word.to_owned());
            }
        } else if !is_known_line(trimmed) {
            return Err(RemovalSimulationFailure::Unrecognised);
        }
    }

    if removed.is_empty() {
        return Err(RemovalSimulationFailure::Unrecognised);
    }
    Ok(RemovalSimulation {
        removed,
        unused: Vec::new(),
    })
}

/// Whether `line` (outside the removal section) is one the dry run is known to print: its
/// progress lines, the count line, the size table and the closing prompt.
fn is_known_line(line: &str) -> bool {
    KNOWN_LINE_PREFIXES
        .iter()
        .any(|prefix| line.starts_with(prefix))
        || is_size_table_line(line)
        || is_count_line(line)
}

/// The count line: a number, `package` or `packages`, and `to remove.`.
fn is_count_line(line: &str) -> bool {
    let mut words = line.split_whitespace();
    let number = words.next();
    let noun = words.next();
    number.is_some_and(|word| word.chars().all(|character| character.is_ascii_digit()))
        && matches!(noun, Some("package" | "packages"))
        && line.ends_with(COUNT_LINE_END)
}

/// A row of the size table: `    -6.1 MiB  |  -    6.1 MiB  released by packages ...`.
fn is_size_table_line(line: &str) -> bool {
    line.starts_with(' ') && line.contains('|')
}

/// Whether `word` is spelled like a package name: ASCII letters and digits, `.`, `_`, `+`, `-`.
fn is_package_name(word: &str) -> bool {
    !word.is_empty()
        && word.chars().all(|character| {
            character.is_ascii_alphanumeric() || matches!(character, '.' | '_' | '+' | '-')
        })
}

impl RemovalFacts for SuseAdapter {
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
#[path = "../tests/suse/suse_removal_tests.rs"]
mod tests;
