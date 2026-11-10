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
    /// A line of the output is outside the grammar the reader knows.
    #[error("line {line} of the package manager's answer is not in a form this agent reads")]
    UnrecognisedLine {
        /// The one-based number of the first line the reader did not know.
        line: usize,
    },
}
