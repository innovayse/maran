//! The temporary database user an imported dump is loaded as.

use maran_agent_core::secret_string::SecretString;

/// The fixed start of every temporary user's name.
const PREFIX: &str = "maran_imp_";

/// How many random bytes make up the name's suffix.
pub const NAME_RANDOM_BYTES: usize = 8;

/// How many random bytes make up the user's password.
pub const PASSWORD_RANDOM_BYTES: usize = 32;

/// How many hex digits follow the prefix: two per random byte.
const SUFFIX_DIGITS: usize = NAME_RANDOM_BYTES * 2;

/// A temporary database user, `maran_imp_<16 hex digits>`.
///
/// The loader creates one per imported dump, grants it every privilege on the
/// one database the dump is for and nothing else, loads the dump as it, and
/// drops it on every path. The name is built here and nowhere else, from an
/// alphabet of lowercase hex digits and underscore, so it can be written inside
/// quotes in a statement without escaping and a leftover user can be found by
/// its prefix.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImportUser(String);

impl ImportUser {
    /// Builds the user from random bytes the caller drew.
    ///
    /// Randomness is an argument, not drawn here, so that a test can pin a name.
    #[must_use]
    pub fn from_random(random: &[u8; NAME_RANDOM_BYTES]) -> Self {
        Self(format!("{PREFIX}{}", hex(random)))
    }

    /// Builds the user's password from random bytes the caller drew.
    ///
    /// Hex digits only, so it can sit inside quotes in a statement and inside
    /// a client option file without escaping. Wrapped at once, so it cannot
    /// reach a log line.
    #[must_use]
    pub fn password_from_random(random: &[u8; PASSWORD_RANDOM_BYTES]) -> SecretString {
        SecretString::new(hex(random))
    }

    /// Reads a user name the database server listed back as one of ours.
    ///
    /// Answers a value only for the exact shape [`Self::from_random`] builds:
    /// the prefix followed by sixteen lowercase hex digits and nothing else. A
    /// name that merely starts with the prefix is not accepted, so a leftover
    /// sweep that drops what this returns can never drop a user it did not
    /// create, and a name that came off the wire cannot carry a quote into a
    /// statement.
    #[must_use]
    pub fn from_listed(name: &str) -> Option<Self> {
        let suffix = name.strip_prefix(PREFIX)?;
        let well_formed = suffix.len() == SUFFIX_DIGITS
            && suffix
                .bytes()
                .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte));

        well_formed.then(|| Self(name.to_owned()))
    }

    /// The user's name, as written between the quotes of an account name.
    #[must_use]
    pub fn name(&self) -> &str {
        &self.0
    }
}

/// Lowercase hexadecimal of `bytes`.
fn hex(bytes: &[u8]) -> String {
    const DIGITS: &[u8; 16] = b"0123456789abcdef";

    let mut text = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        text.push(char::from(DIGITS[usize::from(byte >> 4)]));
        text.push(char::from(DIGITS[usize::from(byte & 0x0f)]));
    }

    text
}

#[cfg(test)]
#[path = "../../tests/backup/import_user_tests.rs"]
mod tests;
