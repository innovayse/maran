//! Which of a child's two output streams something is said about.

use core::fmt;

/// One of the two output streams a child writes to.
///
/// Named in an error so that "the output was too large" says which stream, which
/// is the difference between a tool that is chatty on standard error and one that
/// is flooding the answer a caller parses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OutputStream {
    /// The child's standard output.
    Stdout,
    /// The child's standard error.
    Stderr,
}

impl fmt::Display for OutputStream {
    /// Writes `standard output` or `standard error`.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(match self {
            Self::Stdout => "standard output",
            Self::Stderr => "standard error",
        })
    }
}

#[cfg(test)]
#[path = "../tests/utils/output_stream_tests.rs"]
mod tests;
