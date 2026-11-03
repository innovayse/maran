//! Why a read of the pending package updates did not produce an answer.

/// A reason the agent could not read the pending updates off the package
/// manager's cache (issue #324).
///
/// Every variant means the same thing to a caller: the agent did NOT find out.
/// None of them is "there are no updates", and none may be folded into an empty
/// list — a host whose security updates are pending must never be reported as
/// up to date because the read failed.
///
/// No variant carries a byte the package manager printed. The two numbers a
/// variant holds, an exit status and a line number, are operator information
/// and cannot hold a package name, a path or a tool's wording
/// (rules/rust.md "Errors").
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum PendingUpdatesFailure {
    /// The package manager could not be started at all: the program is absent
    /// or could not be executed. Distinct from a run that was started and
    /// stopped at its deadline, which is a bound the caller reports on its own.
    #[error("the package manager could not be started")]
    NotStarted,
    /// The package manager exited with a status that its documentation does not
    /// give a successful meaning: `apt-get` anything but 0, `dnf check-update`
    /// anything but 0 (none) or 100 (updates), `1` — "cache-only enabled but no
    /// cache" — included.
    #[error("the package manager exited with status {status}")]
    Unsuccessful {
        /// The exit status the package manager returned.
        status: i32,
    },
    /// The package manager's local catalogue has never been built on this host,
    /// so an empty answer would describe an empty cache and not an up-to-date
    /// host. Reported by the Debian family, whose simulation exits 0 with
    /// "0 upgraded" against an empty lists directory.
    #[error("the package catalogue has not been built on this host")]
    CatalogueNotBuilt,
    /// A line of the output does not match the grammar this reader knows. The
    /// whole read fails; the line is never skipped, because a skipped line is
    /// an update the panel never reports.
    #[error("line {line} of the package manager's output is not in a form this agent reads")]
    UnrecognisedLine {
        /// The one-based number of the first line that did not match.
        line: usize,
    },
    /// The output ended without the line that closes it, or a status that says
    /// updates exist came with no row to show for them: the output was cut
    /// short or is not what the status promised.
    #[error("the package manager's output is incomplete")]
    IncompleteOutput,
}
