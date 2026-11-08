//! The request path a redirect rule applies to.

use super::redirect_path_error::RedirectPathError;

/// The longest redirect path accepted, in bytes.
pub const MAXIMUM_LENGTH: usize = 255;

/// The directory the certificate authority's challenge is fetched from.
///
/// nginx matches a redirect location by specificity, and an exact-match rule
/// (`location = ...`) beats the challenge location's `^~` prefix, so a path at
/// or under this directory is refused outright.
const ACME_CHALLENGE_DIRECTORY: &str = "/.well-known/acme-challenge";

/// A validated redirect path: `/` followed by `[A-Za-z0-9._~/-]`, no `.` or
/// `..` segment, no empty segment, at most 255 bytes, never under the
/// certificate challenge directory.
///
/// The path is written into a `location` line. The alphabet holds none of `;`,
/// `{`, `}`, quotes, `$`, backslash or whitespace, so it cannot end the line or
/// open a block (rules/security.md item 4); it is validated, not escaped. The
/// inner value is private and the only constructor is [`RedirectPath::parse`].
///
/// A trailing `/` is allowed and meaningful: a prefix rule needs one, and the
/// operation that pairs a path with its match refuses a prefix path without it.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct RedirectPath(String);

impl RedirectPath {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`RedirectPathError::Empty`] when `candidate` is empty.
    /// - [`RedirectPathError::ControlCharacter`] when it holds a control
    ///   character; checked before everything else so the refusal names the
    ///   real hazard.
    /// - [`RedirectPathError::TooLong`] beyond [`MAXIMUM_LENGTH`] bytes.
    /// - [`RedirectPathError::NotAbsolute`] when it does not start with `/`.
    /// - [`RedirectPathError::ForbiddenCharacter`] for a character outside
    ///   ASCII letters, digits, `.`, `_`, `~`, `/` and `-`.
    /// - [`RedirectPathError::DotSegment`] for a `.` or `..` segment.
    /// - [`RedirectPathError::EmptySegment`] for `//` anywhere in the path.
    /// - [`RedirectPathError::AcmeChallenge`] when the path is, or is under,
    ///   `/.well-known/acme-challenge`.
    pub fn parse(candidate: &str) -> Result<Self, RedirectPathError> {
        if candidate.is_empty() {
            return Err(RedirectPathError::Empty);
        }
        if candidate.chars().any(char::is_control) {
            return Err(RedirectPathError::ControlCharacter);
        }
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(RedirectPathError::TooLong {
                length: candidate.len(),
                maximum: MAXIMUM_LENGTH,
            });
        }
        if !candidate.starts_with('/') {
            return Err(RedirectPathError::NotAbsolute);
        }
        if !candidate.bytes().all(|byte| {
            byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'~' | b'/' | b'-')
        }) {
            return Err(RedirectPathError::ForbiddenCharacter);
        }

        // The first split piece is the empty text before the leading `/`; the
        // last is empty for a trailing `/`. Only an empty piece BETWEEN two
        // slashes is a `//`.
        let segments: Vec<&str> = candidate.split('/').collect();
        let last = segments.len() - 1;
        for (index, segment) in segments.iter().enumerate() {
            if *segment == "." || *segment == ".." {
                return Err(RedirectPathError::DotSegment);
            }
            if segment.is_empty() && index != 0 && index != last {
                return Err(RedirectPathError::EmptySegment);
            }
        }
        // `/` alone splits to two empty pieces and is fine; `//` splits to
        // three and was refused above.

        if candidate == ACME_CHALLENGE_DIRECTORY
            || candidate
                .strip_prefix(ACME_CHALLENGE_DIRECTORY)
                .is_some_and(|rest| rest.starts_with('/'))
        {
            return Err(RedirectPathError::AcmeChallenge);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated path.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/redirect_path_tests.rs"]
mod tests;
