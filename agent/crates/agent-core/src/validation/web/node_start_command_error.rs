//! Why a candidate start command was refused.

use thiserror::Error;

/// Reasons [`super::node_start_command::NodeStartCommand::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NodeStartCommandError {
    /// The candidate was empty or only spaces.
    #[error("a start command cannot be empty")]
    Empty,

    /// The candidate was longer than the bound.
    #[error("a start command cannot exceed {limit} characters")]
    TooLong {
        /// The bound, in characters.
        limit: usize,
    },

    /// A control character — a newline, a carriage return, a NUL, a tab — was found.
    ///
    /// Refused by name and first: the command is written into a unit file, and a newline
    /// there ends the `ExecStart=` line and starts a directive of the caller's choosing in a
    /// file the service manager obeys (rules/security.md §4).
    #[error("a start command cannot contain a control character")]
    ControlCharacter,

    /// The first word is neither `node` nor `npm`.
    #[error("a start command must begin with `node` or `npm`")]
    UnknownProgram,

    /// A character outside the allowed set was found in an argument.
    ///
    /// The set excludes everything a service manager or a shell would treat specially:
    /// `$` and `%` (variable and specifier expansion), quotes, backslash, `;`, `&`, `|`,
    /// redirections, backticks and braces.
    #[error("a start command cannot contain `{character}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
