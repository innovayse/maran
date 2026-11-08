//! The login name of one HTTP basic-authentication user.

use super::basic_auth_user_name_error::BasicAuthUserNameError;

/// The longest user name accepted, in bytes.
pub const MAXIMUM_LENGTH: usize = 32;

/// A validated basic-authentication user name: `[A-Za-z0-9._-]{1,32}`.
///
/// The name is written as the first field of a `name:hash` line in the
/// credential file. The colon is the field separator and a newline ends the
/// line, so a name holding either would add a user or change whose hash a line
/// carries; the alphabet excludes both by construction (rules/security.md
/// item 4). The inner value is private and the only constructor is
/// [`BasicAuthUserName::parse`].
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct BasicAuthUserName(String);

impl BasicAuthUserName {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`BasicAuthUserNameError::Empty`] when `candidate` is empty.
    /// - [`BasicAuthUserNameError::ControlCharacter`] when it holds a control
    ///   character; checked first so the refusal names the real hazard.
    /// - [`BasicAuthUserNameError::TooLong`] beyond [`MAXIMUM_LENGTH`] bytes.
    /// - [`BasicAuthUserNameError::ForbiddenCharacter`] for anything outside
    ///   ASCII letters, digits, `.`, `_` and `-`.
    pub fn parse(candidate: &str) -> Result<Self, BasicAuthUserNameError> {
        if candidate.is_empty() {
            return Err(BasicAuthUserNameError::Empty);
        }
        if candidate.chars().any(char::is_control) {
            return Err(BasicAuthUserNameError::ControlCharacter);
        }
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(BasicAuthUserNameError::TooLong {
                length: candidate.len(),
                maximum: MAXIMUM_LENGTH,
            });
        }
        if !candidate
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'-'))
        {
            return Err(BasicAuthUserNameError::ForbiddenCharacter);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated name.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/basic_auth_user_name_tests.rs"]
mod tests;
