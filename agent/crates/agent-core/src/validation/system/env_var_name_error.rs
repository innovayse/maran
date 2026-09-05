//! Why a cron environment variable name was refused.

/// Reasons [`super::env_var_name::EnvVarName::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum EnvVarNameError {
    /// The candidate was empty.
    #[error("an environment variable name cannot be empty")]
    Empty,

    /// The candidate was longer than the accepted 64 characters.
    #[error("an environment variable name cannot exceed {maximum} characters")]
    TooLong {
        /// The ceiling that was exceeded.
        maximum: usize,
    },

    /// A character outside `A-Z`, `0-9` and `_` was found.
    ///
    /// Lowercase is refused with everything else: cron matches these names
    /// exactly, so `mailto` would be a name the denylist below never sees while
    /// looking, to a reader of the crontab, exactly like the one it refuses.
    /// The value is also written into a `KEY=value` line in a root-installed
    /// crontab, so a newline or an `=` here is the injection rules/security.md
    /// §4 is about.
    #[error("an environment variable name cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// The name began with a digit.
    ///
    /// The shell's own rule, and cron's: a name starts with a letter or an
    /// underscore, so `1PATH=x` is not an assignment at all.
    #[error("an environment variable name cannot begin with a digit")]
    LeadingDigit,

    /// The name is one the agent reserves for itself.
    ///
    /// `MAILTO` and `SHELL`, and each for its own reason:
    ///
    /// - `MAILTO` tells cron where to mail an entry's output. The agent already
    ///   captures that output to a file the panel reads, so a customer-set
    ///   `MAILTO` buys nothing and hands them an outbound relay through the
    ///   host's MTA — spam sent from the server's own address.
    /// - `SHELL` chooses the interpreter cron runs every entry under. The
    ///   installed line names `/bin/sh` explicitly for exactly that reason, and
    ///   a `SHELL` assignment would change the interpreter under entries the
    ///   agent promised were run by that one.
    ///
    /// The agent writes both itself, as the first two lines of the managed
    /// region, so this is a name collision as much as a privilege question:
    /// there is already a value there and it is not the customer's.
    #[error("`{name}` is reserved by the agent and cannot be set")]
    ReservedName {
        /// The reserved name that was offered.
        name: String,
    },
}
