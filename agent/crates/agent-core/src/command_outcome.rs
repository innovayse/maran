//! What running one program produced.

/// What running one program produced.
///
/// Shared by every `ops` area that spawns a process and reads its result —
/// `accounts` (`useradd`, `usermod`, …) and `safe_write` (validators,
/// reloads) both need exactly this shape, which is what moved it here rather
/// than letting each area keep its own copy (rules/rust.md "Operation
/// anatomy": "a type needed by two areas moves to `agent-core`; areas never
/// import each other").
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CommandOutcome {
    /// The exit status, or -1 when the process was killed by a signal.
    pub status: i32,
    /// Everything the program wrote to standard output.
    pub stdout: String,
    /// Everything the program wrote to standard error.
    pub stderr: String,
}
