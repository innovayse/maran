//! The dry run of a package removal on the Debian family.

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;

use crate::debian::DebianAdapter;
use crate::removal_facts::RemovalFacts;
use crate::removal_simulation::RemovalSimulation;
use crate::removal_simulation_failure::RemovalSimulationFailure;

/// What `apt-get -s remove` prints in front of the name of each package it would remove.
const REMOVE_LINE_MARKER: &str = "Remv ";

/// The fixed progress lines of every run.
const PROGRESS_LINES: [&str; 3] = [
    "Reading package lists...",
    "Building dependency tree...",
    "Reading state information...",
];

/// The header of the list of dependencies nothing needs once the removal is done, and the hint
/// that follows it. `apt-get remove` lists them and does not remove them.
const AUTOREMOVABLE_HEADER: &str =
    "The following packages were automatically installed and are no longer required:";

/// The hint printed after that list.
const AUTOREMOVE_HINT_PREFIX: &str = "Use 'apt autoremove' to remove them.";

/// The header of the list of packages the run would remove.
const REMOVED_HEADER_END: &str = "be REMOVED:";

/// What the plan's count line ends with after its first two counts.
const COUNT_LINE_MARKER: &str = " upgraded, ";

/// The arguments of the dry run: `-s` is a simulation, nothing is removed or configured, and
/// `Debug::NoLocking` keeps it from taking the package database lock a real run is waiting on
/// (the same two options as the pending-updates simulation).
#[must_use]
pub fn removal_simulation_arguments(package: &str) -> Vec<String> {
    [
        "-s",
        "-q",
        "-o",
        "Debug::NoLocking=true",
        "remove",
        "--",
        package,
    ]
    .iter()
    .map(|argument| (*argument).to_owned())
    .collect()
}

/// Reads `apt-get -s remove`: one `Remv <name> [<version>] ...` line per package that would be
/// removed, the requested one and its dependents alike. The architecture qualifier a name may
/// carry (`libfoo:amd64`) is dropped.
///
/// `apt-get remove` never removes a package for being unused, so the second set is empty.
///
/// The reader fails closed: besides the lines above, a package list is read only under the two
/// headers it knows (the removal list and the autoremovable list), and every other line, an
/// `Inst`, `Conf` or `Purg` simulation line, another package list, or a count line that reports
/// an upgrade or an install, makes the read `Unrecognised`: a removal that installs or upgrades
/// something is not the removal this check was written for.
///
/// Recorded: `apt-get -s remove curl` on `ubuntu:24.04` (`apt_remove_simulation.txt`).
///
/// # Errors
///
/// Returns [`RemovalSimulationFailure::Unsuccessful`] for a status other than 0 and
/// [`RemovalSimulationFailure::Unrecognised`] for output with no `Remv` line or with a line the
/// reader does not know.
pub fn read_removal_simulation(
    status: i32,
    stdout: &str,
) -> Result<RemovalSimulation, RemovalSimulationFailure> {
    if status != 0 {
        return Err(RemovalSimulationFailure::Unsuccessful { status });
    }

    let mut removed: Vec<String> = Vec::new();
    let mut in_list = false;
    for line in stdout.lines() {
        let trimmed = line.trim_end();
        if let Some(rest) = trimmed.strip_prefix(REMOVE_LINE_MARKER) {
            let name = rest
                .split_whitespace()
                .next()
                .ok_or(RemovalSimulationFailure::Unrecognised)?;
            removed.push(name.split(':').next().unwrap_or(name).to_owned());
            in_list = false;
        } else if trimmed.starts_with(' ') {
            // The names under a list header; indented text anywhere else is not a list.
            if !in_list {
                return Err(RemovalSimulationFailure::Unrecognised);
            }
        } else if trimmed.ends_with(REMOVED_HEADER_END) || trimmed == AUTOREMOVABLE_HEADER {
            in_list = true;
        } else if PROGRESS_LINES.contains(&trimmed)
            || trimmed.starts_with(AUTOREMOVE_HINT_PREFIX)
            || is_clean_count_line(trimmed)
        {
            in_list = false;
        } else {
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

/// The count line of a removal that upgrades and installs nothing:
/// `0 upgraded, 0 newly installed, 1 to remove and 67 not upgraded.`.
fn is_clean_count_line(line: &str) -> bool {
    line.starts_with('0')
        && line.contains(COUNT_LINE_MARKER)
        && line.contains(", 0 newly installed, ")
        && line.ends_with("not upgraded.")
}

impl RemovalFacts for DebianAdapter {
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

    fn nodejs_removal_simulation_arguments(&self, _stream: &NodejsModuleStream) -> Vec<String> {
        Vec::new()
    }
}

#[cfg(test)]
#[path = "../tests/debian/debian_removal_tests.rs"]
mod tests;
