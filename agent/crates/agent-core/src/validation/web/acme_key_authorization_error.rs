//! Why an ACME key authorization was refused.

/// Reasons [`super::acme_key_authorization::AcmeKeyAuthorization::parse`]
/// refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AcmeKeyAuthorizationError {
    /// The candidate was longer than the ceiling.
    #[error("a key authorization is at most {maximum} bytes, not {actual}")]
    WrongLength {
        /// The most bytes a key authorization may have.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// The candidate does not begin with the token it is written for and a dot.
    #[error("the key authorization does not answer this token")]
    NotForToken,

    /// What follows the dot is empty or not base64url.
    #[error("the key authorization's thumbprint is not base64url")]
    MalformedThumbprint,
}
