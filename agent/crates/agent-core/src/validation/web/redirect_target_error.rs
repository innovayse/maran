//! Why a candidate redirect target was refused.

use super::domain_error::DomainError;

/// Reasons [`super::redirect_target::RedirectTarget::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RedirectTargetError {
    /// The candidate was empty.
    #[error("a redirect target cannot be empty")]
    Empty,

    /// The candidate held a control character (a newline included), which would
    /// end the directive and start another.
    #[error("a redirect target cannot contain a control character")]
    ControlCharacter,

    /// The candidate was longer than the limit.
    #[error("a redirect target is at most {maximum} bytes, this one has {length}")]
    TooLong {
        /// The length offered, in bytes.
        length: usize,
        /// The longest accepted.
        maximum: usize,
    },

    /// The candidate held a character outside the set a redirect target may
    /// use: `;`, `{`, `}`, quotes, `$`, backslash, `#` and whitespace land here.
    #[error("a redirect target holds a character that is not allowed")]
    ForbiddenCharacter,

    /// The candidate started with `//`, a protocol-relative URL that takes the
    /// scheme of the request and so can leave a secure site for an insecure one.
    #[error("a redirect target cannot start with `//`")]
    ProtocolRelative,

    /// The candidate was neither a path starting with `/` nor an `http://` or
    /// `https://` URL (`javascript:`, `data:`, `ftp:` and the like).
    #[error("a redirect target must be a path starting with `/` or an http(s) URL")]
    UnsupportedScheme,

    /// The URL carried user information (`user@host`), the shape of a
    /// look-alike link.
    #[error("a redirect target cannot contain user information (`@` before the host)")]
    UserInformation,

    /// The URL's host was not a valid host name.
    #[error("the redirect target's host is not valid: {0}")]
    InvalidHost(#[from] DomainError),

    /// The URL's port was not a number in `1..=65535`.
    #[error("the redirect target's port is not valid")]
    InvalidPort,
}
