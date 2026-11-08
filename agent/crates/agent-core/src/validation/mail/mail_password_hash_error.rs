//! Why a candidate mailbox password hash was refused.

use crate::validation::web::bcrypt_hash_error::BcryptHashError;

use super::mail_password_scheme::MailPasswordScheme;

/// Reasons [`super::mail_password_hash::MailPasswordHash::parse`] refuses a
/// candidate.
///
/// No variant carries the candidate: a refused hash is still a credential, and
/// an error that is logged must not echo it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailPasswordHashError {
    /// The candidate held a control character (a newline included), which
    /// would end the login database's line and start another.
    #[error("a password hash cannot contain a control character")]
    ControlCharacter,

    /// The candidate is longer than any accepted hash.
    #[error("a password hash cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },

    /// The candidate is neither Argon2id nor bcrypt. The weak schemes — MD5,
    /// SHA-1, plain crypt, a bare digest — are refused here by name of what
    /// they are not (rules/security.md item 9).
    #[error("the password hash is not in a supported scheme")]
    UnsupportedScheme,

    /// The candidate is in a supported scheme, but not the one this host's
    /// IMAP server verifies. Accepting it would store a credential nobody can
    /// log in with.
    #[error("this host verifies only {expected:?} password hashes")]
    SchemeNotVerifiedHere {
        /// The scheme this host's IMAP server verifies.
        expected: MailPasswordScheme,
    },

    /// An Argon2id hash whose version, parameter list or fields are not in the
    /// encoded form.
    #[error("the Argon2id hash is malformed")]
    MalformedArgon2id,

    /// An Argon2id hash whose memory, time or parallelism cost is outside the
    /// accepted range. Too low makes a stolen database cheap to attack; too
    /// high makes one login cost the IMAP server a noticeable share of its memory.
    #[error("the Argon2id cost parameters are outside the accepted range")]
    Argon2idCostOutOfRange,

    /// A bcrypt hash that the bcrypt reading refused.
    #[error("invalid bcrypt hash: {0}")]
    Bcrypt(BcryptHashError),
}
