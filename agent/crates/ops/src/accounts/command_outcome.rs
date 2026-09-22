//! What running one program produced.

/// What running one program produced.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CommandOutcome {
    /// The exit status, or -1 when the process was killed by a signal.
    pub status: i32,
    /// Everything the program wrote to standard output.
    pub stdout: String,
    /// Everything the program wrote to standard error.
    pub stderr: String,
}
