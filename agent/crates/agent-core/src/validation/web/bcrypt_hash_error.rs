//! Why a candidate password hash was refused.

/// Reasons [`super::bcrypt_hash::BcryptHash::parse`] refuses a candidate.
///
/// No variant carries the candidate: a refused hash is still a credential, and
/// an error that is logged must not echo it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BcryptHashError {
    /// The candidate held a control character (a newline included), which would
    /// end the credential line and start another.
    #[error("a password hash cannot contain a control character")]
    ControlCharacter,

    /// The candidate was not the 60 bytes a bcrypt hash has.
    #[error("a bcrypt hash is {expected} bytes, this one has {actual}")]
    WrongLength {
        /// The length offered, in bytes.
        actual: usize,
        /// The length a bcrypt hash has.
        expected: usize,
    },

    /// The candidate did not start with `$2a$`, `$2b$` or `$2y$`.
    ///
    /// Every other scheme is refused, the weak ones by name: `$apr1$` (MD5),
    /// `{SHA}` (SHA-1) and plain crypt are not acceptable for a credential
    /// (rules/security.md item 9).
    #[error("only bcrypt hashes (`$2a$`, `$2b$`, `$2y$`) are accepted")]
    UnsupportedScheme,

    /// The cost field was not two digits in `10..=14`.
    #[error("the bcrypt cost must be between {minimum} and {maximum}")]
    CostOutOfRange {
        /// The lowest cost accepted.
        minimum: u8,
        /// The highest cost accepted.
        maximum: u8,
    },

    /// The salt-and-hash part held a character outside the bcrypt alphabet
    /// (`.`, `/`, letters, digits), or the cost was not followed by `$`.
    #[error("the hash is not in the bcrypt alphabet")]
    ForbiddenCharacter,
}
