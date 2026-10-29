//! Why a program run through the bounded spawn core produced no outcome.

use crate::utils::bound_exceeded::BoundExceeded;

/// The program could not be run to a [`crate::command_outcome::CommandOutcome`].
///
/// A program that started and exited non-zero is NOT an error: its status is
/// the caller's domain decision and comes back as an outcome like any success.
/// These are the cases where there is no outcome to decide on.
///
/// Error text is for operators and logs and carries no argument vector, no
/// captured output and no input byte: an argument can be a path inside a
/// customer's home, and the input can be a password or a private key.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SpawnError {
    /// The program could not be started at all: not found, not executable, the
    /// fork was refused.
    #[error("could not start {program}: {reason}")]
    Start {
        /// The absolute path of the program.
        program: String,
        /// The operating system's reason.
        reason: String,
    },
    /// The input could not be delivered to the child's standard input, and the
    /// child exited `0` without it.
    ///
    /// A child that fails to take its input and then exits non-zero is not this
    /// error: its status and standard error come back as an outcome, since that
    /// is the child's own verdict and the caller maps it.
    ///
    /// Deliberately carries no I/O error text and no byte of the input: the
    /// input is a credential or a key for every caller that supplies one.
    #[error("could not deliver input to {program}")]
    Input {
        /// The absolute path of the program.
        program: String,
    },
    /// The child was running but could not be waited for, or its output could
    /// not be read.
    #[error("could not wait for {program}: {reason}")]
    Wait {
        /// The absolute path of the program.
        program: String,
        /// What failed, without any captured output.
        reason: String,
    },
    /// A deadline or an output cap was reached and the process group was killed.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),
}

#[cfg(test)]
#[path = "../tests/utils/spawn_error_tests.rs"]
mod tests;
