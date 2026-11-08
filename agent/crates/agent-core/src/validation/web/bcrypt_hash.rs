//! A password stored as a bcrypt hash, for HTTP basic authentication.

use super::bcrypt_hash_error::BcryptHashError;
use crate::secret_string::SecretString;

/// The length of a bcrypt hash: `$2y$` (4) + cost (2) + `$` (1) + salt and
/// hash (53).
const LENGTH: usize = 60;

/// The lowest cost accepted. Below it a stolen file is cheap to attack.
const MINIMUM_COST: u8 = 10;

/// The highest cost accepted. Above it a single request that authenticates
/// costs the web server a noticeable fraction of a second.
const MAXIMUM_COST: u8 = 14;

/// A validated bcrypt hash: `$2a$`, `$2b$` or `$2y$`, a two-digit cost in
/// `10..=14`, `$`, and 53 characters of `./A-Za-z0-9`.
///
/// The panel hashes a password and sends only the hash; the agent checks its
/// SHAPE and never sees a password. The shape is what makes the value safe to
/// write as the second field of a `name:hash` line: the alphabet holds neither
/// `:` nor a newline (rules/security.md item 4), and the scheme list leaves
/// out every weak one (item 9).
///
/// The hash is a credential at rest. It is held in a [`SecretString`], so its
/// `Debug` and `Display` print `«redacted»` and a request struct that derives
/// `Debug` cannot leak it into a log line. [`BcryptHash::expose`] is the only
/// reader and is named to be greppable.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BcryptHash(SecretString);

impl BcryptHash {
    /// Validates `candidate` as a bcrypt hash and wraps it.
    ///
    /// # Errors
    ///
    /// - [`BcryptHashError::ControlCharacter`] when `candidate` holds a control
    ///   character; checked first.
    /// - [`BcryptHashError::WrongLength`] when it is not 60 bytes.
    /// - [`BcryptHashError::UnsupportedScheme`] when it does not start with
    ///   `$2a$`, `$2b$` or `$2y$`.
    /// - [`BcryptHashError::CostOutOfRange`] when the cost is not two digits in
    ///   `10..=14`.
    /// - [`BcryptHashError::ForbiddenCharacter`] when the cost is not followed
    ///   by `$`, or the rest holds a byte outside the bcrypt alphabet.
    pub fn parse(candidate: &str) -> Result<Self, BcryptHashError> {
        if candidate.chars().any(char::is_control) {
            return Err(BcryptHashError::ControlCharacter);
        }
        if candidate.len() != LENGTH {
            return Err(BcryptHashError::WrongLength {
                actual: candidate.len(),
                expected: LENGTH,
            });
        }

        let bytes = candidate.as_bytes();
        if !(bytes.starts_with(b"$2a$") || bytes.starts_with(b"$2b$") || bytes.starts_with(b"$2y$"))
        {
            return Err(BcryptHashError::UnsupportedScheme);
        }

        // Positions 4 and 5 are the cost digits, 6 is the `$` after them.
        let cost = match (bytes[4], bytes[5]) {
            (tens, ones) if tens.is_ascii_digit() && ones.is_ascii_digit() => {
                (tens - b'0') * 10 + (ones - b'0')
            }
            _ => {
                return Err(BcryptHashError::CostOutOfRange {
                    minimum: MINIMUM_COST,
                    maximum: MAXIMUM_COST,
                });
            }
        };
        if !(MINIMUM_COST..=MAXIMUM_COST).contains(&cost) {
            return Err(BcryptHashError::CostOutOfRange {
                minimum: MINIMUM_COST,
                maximum: MAXIMUM_COST,
            });
        }
        if bytes[6] != b'$' {
            return Err(BcryptHashError::ForbiddenCharacter);
        }

        if !bytes[7..]
            .iter()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'/'))
        {
            return Err(BcryptHashError::ForbiddenCharacter);
        }

        Ok(Self(SecretString::new(candidate.to_owned())))
    }

    /// The hash, for the one place that writes it into the credential file.
    ///
    /// Named after what it does, and the only reader: a search for it lists
    /// every place the hash leaves its wrapper.
    #[must_use]
    pub fn expose(&self) -> &str {
        self.0.expose()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/bcrypt_hash_tests.rs"]
mod tests;
