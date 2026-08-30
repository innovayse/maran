//! Why the agent could not make sense of its command line.

/// Rejection reasons for [`super::agent_options::AgentOptions::parse`].
///
/// Both variants stop the agent from starting. The alternative — carrying on with
/// a default — would mean a mistyped unit file produces a running daemon with an
/// access rule nobody wrote, which is worse than a service that fails loudly.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum OptionsError {
    /// A flag was given with no value after it.
    #[error("{flag} requires a value")]
    MissingValue {
        /// The flag that was left dangling.
        flag: &'static str,
    },
    /// The value of `--allow-uid` is not a uid.
    #[error("--allow-uid expects a number, got '{value}'")]
    InvalidUid {
        /// The value that could not be parsed.
        value: String,
    },
}
