//! Failures of the account operations.

use maran_agent_core::validation::name_error::NameError;

/// What can go wrong while managing an account's operating-system identity.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum AccountError {
    /// The name is not one this agent will turn into a system user.
    ///
    /// Raised by the agent's own revalidation, not by the API's: a name reaching
    /// here becomes a user, a home directory and a path segment, so it is checked
    /// where it is used rather than where it was received.
    #[error("invalid account name")]
    InvalidName(#[from] NameError),

    /// The account already exists on this host.
    #[error("account '{username}' already exists")]
    AlreadyExists {
        /// The name that was asked for.
        username: String,
    },

    /// The account does not exist on this host.
    #[error("account '{username}' was not found")]
    NotFound {
        /// The name that was looked up.
        username: String,
    },

    /// A system command exited non-zero.
    ///
    /// Carries the program and its stderr because an operator reading the agent's
    /// log needs to know which tool refused and why; the text never reaches a
    /// customer (rules/security.md item 8).
    #[error("{program} failed with status {status}: {stderr}")]
    CommandFailed {
        /// The program that was run.
        program: String,
        /// Its exit status.
        status: i32,
        /// Its standard error, trimmed.
        stderr: String,
    },

    /// A system command could not be run at all — usually because it is not installed.
    #[error("could not run {program}: {reason}")]
    CommandUnavailable {
        /// The program that could not be started.
        program: String,
        /// Why it could not be started.
        reason: String,
    },

    /// A command's output did not have the shape this agent knows how to read.
    #[error("could not read the output of {program}")]
    UnreadableOutput {
        /// The program whose output could not be parsed.
        program: String,
    },
}
