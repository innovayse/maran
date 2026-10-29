//! What the core does with a spawned child's output streams.

/// Whether the child's output is captured, and under what ceiling.
///
/// A capture has a cap per stream and reaching it is an ERROR
/// ([`crate::utils::bound_exceeded::BoundExceeded::OutputTooLarge`]), never a
/// shortened answer: the readers stop RETAINING at the cap, so the daemon never
/// holds more than the cap per stream, and the child's process group is killed.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChildOutput {
    /// Capture both streams, each under its own cap in bytes. Exactly the cap is
    /// accepted; one byte more is refused.
    Capture {
        /// The most standard output that is accepted.
        stdout_cap: u64,
        /// The most standard error that is accepted.
        stderr_cap: u64,
    },
    /// Throw both streams away at the descriptor (`/dev/null`). The outcome's
    /// strings are empty and no size is ever refused.
    Discard,
}

impl ChildOutput {
    /// The cap per stream the default applies: 4 MiB.
    ///
    /// Orders of magnitude above anything the ordinary callers read (a state
    /// query, a listing of rules, a tool's diagnostic) and bounded per call:
    /// at most 8 MiB is retained (4 MiB on each of two streams), so if all 512 blocking
    /// threads held a full capture at once the agent would hold about 4 GiB. That worst
    /// case needs every thread running a program that floods both streams, which no
    /// caller of this agent does; a caller that expects more passes its own cap.
    pub const DEFAULT_CAP: u64 = 4 * 1024 * 1024;

    /// Both streams captured under [`Self::DEFAULT_CAP`].
    pub const DEFAULT: Self = Self::Capture {
        stdout_cap: Self::DEFAULT_CAP,
        stderr_cap: Self::DEFAULT_CAP,
    };
}

#[cfg(test)]
#[path = "../tests/utils/child_output_tests.rs"]
mod tests;
