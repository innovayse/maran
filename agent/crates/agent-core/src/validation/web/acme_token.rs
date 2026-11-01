//! The token of one ACME HTTP-01 challenge, which names a file root writes.

use super::acme_token_error::AcmeTokenError;

/// The most characters a token may have.
///
/// RFC 8555 §8.3 asks for at least 128 bits of entropy, which base64url spells
/// in 22 characters; authorities send 43. The ceiling is generous and exists so
/// that a remote party cannot name a file longer than the filesystem accepts.
const MAXIMUM_LENGTH: usize = 128;

/// A validated ACME challenge token.
///
/// The token comes from the certificate authority — a REMOTE party — and the
/// agent writes a file named after it, as root, into
/// [`crate::agent_paths::AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY`]. RFC 8555
/// defines it as base64url, and that alphabet is the whole defence: with no
/// `.`, no `/` and no NUL in it, the joined path cannot leave the directory.
/// The inner string is private and [`AcmeToken::parse`] is the only
/// constructor, so holding one is proof the check ran.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AcmeToken(String);

impl AcmeToken {
    /// Validates `candidate` as a challenge token and wraps it.
    ///
    /// # Errors
    ///
    /// - [`AcmeTokenError::WrongLength`] for the empty token or one longer than
    ///   128 bytes.
    /// - [`AcmeTokenError::IllegalCharacter`] for anything outside `A-Z`,
    ///   `a-z`, `0-9`, `-` and `_` — a dot and a slash included, which is what
    ///   keeps the file inside its directory.
    pub fn parse(candidate: &str) -> Result<Self, AcmeTokenError> {
        if candidate.is_empty() || candidate.len() > MAXIMUM_LENGTH {
            return Err(AcmeTokenError::WrongLength {
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        if let Some(character) = candidate
            .chars()
            .find(|character| !Self::is_alphabet(*character))
        {
            return Err(AcmeTokenError::IllegalCharacter { character });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated token, which is also the challenge file's name.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// Whether `character` belongs to the base64url alphabet (RFC 4648 §5),
    /// the one alphabet a token and a key authorization's thumbprint share.
    pub(crate) fn is_alphabet(character: char) -> bool {
        character.is_ascii_alphanumeric() || character == '-' || character == '_'
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/acme_token_tests.rs"]
mod tests;
