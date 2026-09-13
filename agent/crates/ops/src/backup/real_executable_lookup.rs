//! The [`ExecutableLookup`] this agent actually runs with.

use std::os::unix::fs::PermissionsExt as _;

use crate::backup::executable_lookup::ExecutableLookup;

/// Asks the real filesystem, the same way
/// `agent/tests/binary_paths_on_a_real_host.rs` does: a regular file, and at
/// least one of its three execute bits set.
///
/// `stat`-and-executable rather than actually trying to run the program: a
/// preflight that spawned `tar --version` to prove `tar` runs would itself be
/// a process spawn on the root daemon's start path, for a question the
/// metadata already answers, and would need its own timeout and its own
/// argument list to stay inert.
pub struct RealExecutableLookup;

impl ExecutableLookup for RealExecutableLookup {
    fn is_executable(&self, path: &str) -> bool {
        std::fs::metadata(path)
            .map(|metadata| metadata.is_file() && metadata.permissions().mode() & 0o111 != 0)
            .unwrap_or(false)
    }
}

#[cfg(test)]
#[path = "../tests/backup/real_executable_lookup_tests.rs"]
mod tests;
