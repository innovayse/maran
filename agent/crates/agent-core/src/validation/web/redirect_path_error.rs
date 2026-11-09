//! Why a candidate redirect path was refused.

/// Reasons [`super::redirect_path::RedirectPath::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RedirectPathError {
    /// The candidate was empty.
    #[error("a redirect path cannot be empty")]
    Empty,

    /// The candidate held a control character (a newline included), which would
    /// end the directive and start another.
    #[error("a redirect path cannot contain a control character")]
    ControlCharacter,

    /// The candidate was longer than the limit.
    #[error("a redirect path is at most {maximum} bytes, this one has {length}")]
    TooLong {
        /// The length offered, in bytes.
        length: usize,
        /// The longest accepted.
        maximum: usize,
    },

    /// The candidate did not start with `/`.
    #[error("a redirect path must start with `/`")]
    NotAbsolute,

    /// The candidate held a character outside `[A-Za-z0-9._~/-]`.
    ///
    /// This refuses by what is allowed rather than by a list of what is not:
    /// `;`, `{`, `}`, quotes, `$`, spaces, `%`, `?` and `#` all land here.
    #[error("a redirect path may hold only letters, digits, `.`, `_`, `~`, `/` and `-`")]
    ForbiddenCharacter,

    /// A segment of the path was `.` or `..`, which nginx normalises away
    /// before matching, so the rule would never apply.
    #[error("a redirect path cannot contain a `.` or `..` segment")]
    DotSegment,

    /// The path held `//`, which nginx merges into `/` before matching, so the
    /// rule would never apply.
    #[error("a redirect path cannot contain an empty segment (`//`)")]
    EmptySegment,

    /// The path is, or lies under, the ACME challenge directory.
    ///
    /// An exact-match redirect there would outrank the challenge location and
    /// take certificate issuance and renewal down for the site.
    #[error("a redirect path cannot be under the certificate challenge directory")]
    AcmeChallenge,
}
