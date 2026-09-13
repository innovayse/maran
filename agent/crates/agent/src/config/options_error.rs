//! Why the agent could not make sense of its command line.

/// Rejection reasons for [`super::invocation::Invocation::parse`].
///
/// Every variant stops the agent from starting. The alternative — carrying on with
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
    /// An argument this binary does not define.
    ///
    /// Refused rather than skipped. An ignored flag means the process runs with
    /// whatever the REST of the command line said, which for this daemon is a
    /// socket path and an access rule — so a mistyped or unsupported flag would
    /// silently produce a root daemon serving a configuration nobody wrote.
    #[error("unknown argument '{flag}'")]
    UnknownFlag {
        /// The argument that was not recognised.
        flag: String,
    },
    /// The value of `--allow-uid` is not a uid.
    #[error("--allow-uid expects a number, got '{value}'")]
    InvalidUid {
        /// The value that could not be parsed.
        value: String,
    },
    /// The value of a port flag is not a port number.
    ///
    /// Its own variant rather than a second use of [`Self::InvalidUid`],
    /// because the two refuse for different reasons and an operator needs the
    /// right one: a uid may legitimately be 0, and a port may not — 0 is what
    /// an absent field decodes to and what a firewall reads as "any port".
    ///
    /// It names the flag as well as the value, because the render subcommand
    /// takes two port flags whose meanings are not interchangeable: rendering
    /// SSH's hard allow for the panel's port and the panel's for SSH's locks
    /// the operator out of the host and the panel at once.
    #[error("{flag} expects a port between 1 and 65535, got '{value}'")]
    InvalidPort {
        /// The flag whose value was refused.
        flag: &'static str,
        /// The value that could not be parsed as a port.
        value: String,
    },
    /// A flag that names one thing was given twice.
    ///
    /// Refused rather than resolved by last-wins, for the reason every refusal
    /// in this file exists: a caller who passes a flag twice and a binary that
    /// silently keeps one of the values disagree about what is being started,
    /// and the disagreement surfaces later as a configuration nobody wrote.
    /// Flags that legitimately repeat — `--ssh-port`, which names one of
    /// several ports a host's sshd can listen on — accumulate instead and never
    /// reach this.
    #[error("{flag} was given more than once, and names only one value")]
    RepeatedFlag {
        /// The flag that was repeated.
        flag: &'static str,
    },
}
