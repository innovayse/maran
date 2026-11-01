//! The content of one ACME HTTP-01 challenge file.

use super::acme_key_authorization_error::AcmeKeyAuthorizationError;
use super::acme_token::AcmeToken;

/// The most characters a key authorization may have: a token at its own
/// ceiling, one dot, and a SHA-256 thumbprint in base64url (43 characters),
/// with room to spare.
const MAXIMUM_LENGTH: usize = 256;

/// A validated key authorization for one token.
///
/// RFC 8555 §8.1 defines it as `token || '.' || base64url(thumbprint)`. The
/// agent serves it to the certificate authority as the body of the challenge
/// file, so it is held to exactly that grammar: the token it answers, one dot,
/// and base64url. No newline, no control character and nothing a browser
/// could read as markup can be constructed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AcmeKeyAuthorization(String);

impl AcmeKeyAuthorization {
    /// Validates `candidate` as the key authorization answering `token`.
    ///
    /// # Errors
    ///
    /// - [`AcmeKeyAuthorizationError::WrongLength`] beyond 256 bytes.
    /// - [`AcmeKeyAuthorizationError::NotForToken`] when it does not begin
    ///   with `token` followed by one dot — an answer for another challenge.
    /// - [`AcmeKeyAuthorizationError::MalformedThumbprint`] when what follows
    ///   the dot is empty or not base64url.
    pub fn parse(token: &AcmeToken, candidate: &str) -> Result<Self, AcmeKeyAuthorizationError> {
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(AcmeKeyAuthorizationError::WrongLength {
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        let Some(thumbprint) = candidate
            .strip_prefix(token.as_str())
            .and_then(|rest| rest.strip_prefix('.'))
        else {
            return Err(AcmeKeyAuthorizationError::NotForToken);
        };

        if thumbprint.is_empty() || !thumbprint.chars().all(AcmeToken::is_alphabet) {
            return Err(AcmeKeyAuthorizationError::MalformedThumbprint);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated key authorization, which is the challenge file's content.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/acme_key_authorization_tests.rs"]
mod tests;
