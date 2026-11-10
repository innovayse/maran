//! Why a remote path was refused.

/// Reasons [`super::remote_path::RemotePath::parse`] refuses a candidate.
///
/// No variant carries the refused text: it came from a remote server the agent
/// does not trust, and an operator log line needs to know WHICH rule refused it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RemotePathError {
    /// The candidate was empty.
    #[error("a remote path cannot be empty")]
    Empty,

    /// The candidate is longer than the 4096 bytes Linux allows a path.
    #[error("a remote path cannot exceed {limit} bytes")]
    TooLong {
        /// The ceiling it was measured against.
        limit: usize,
    },

    /// The candidate does not start with `/`.
    #[error("a remote path must be absolute")]
    NotAbsolute,

    /// A character outside the allow-list `[A-Za-z0-9._/@:+-]`: whitespace, a
    /// quote, a glob or shell metacharacter, a control character, a non-ASCII
    /// character.
    #[error("a remote path contains a character outside the allowed set")]
    IllegalCharacter,

    /// Two consecutive `/`, which would make the path ambiguous.
    #[error("a remote path cannot contain an empty component")]
    EmptyComponent,

    /// A component is `..`.
    #[error("a remote path cannot contain a parent-directory component")]
    ParentComponent,

    /// A component starts with `-`, which a command on the remote side could
    /// read as an option.
    #[error("a remote path component cannot start with a dash")]
    LeadingDash,
}
