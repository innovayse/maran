//! Why a read of what the package manager OFFERS produced no answer.

/// Why [`crate::PackageFacts::read_package_candidates`] could not tell which
/// packages the host's package manager offers.
///
/// Carries an exit status or a line number and never a byte the package
/// manager printed, so it can be logged and returned without redaction. A
/// failure is never an empty answer: "the host offers nothing" and "the host
/// could not be asked" are different facts, and reading the second as the first
/// would hide every version from an operator whose package cache is merely
/// unreadable.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum PackageQueryFailure {
    /// The program exited with a status the family's reader treats as a failure
    /// of the query itself: a cache never built, a repository that could not be
    /// read, a program that was not started.
    #[error("the package manager's query exited with status {status}")]
    Unsuccessful {
        /// The exit status the program ended with.
        status: i32,
    },
    /// The sources carry no module streams at all: the listing exited unsuccessfully with the
    /// package manager's own "No matching Modules" answer and a metadata cache that exists (a
    /// release without modularity, which the RHEL 10 generation is, issue #532; it is not asked,
    /// because it packages Node.js per major, issue #542). A different
    /// fact from [`Self::Unsuccessful`], where the package manager could not be asked.
    #[error("the package sources carry no module streams")]
    NoModules,
    /// A line of the output is outside the grammar the reader knows.
    #[error("line {line} of the package manager's answer is not in a form this agent reads")]
    UnrecognisedLine {
        /// The one-based number of the first line the reader did not know.
        line: usize,
    },
}
