//! The operator-chosen leading part of every object key a destination writes.

use super::s3_object_prefix_error::S3ObjectPrefixError;

/// The most characters a prefix may have.
///
/// An object key is bounded at 1024 bytes by the stores this talks to, and the
/// agent-derived remainder — `/<account>/<backup id>.tar.gz` — is bounded too,
/// so a prefix this long leaves room the key can never run out of.
const MAXIMUM_LENGTH: usize = 256;

/// A validated object-key prefix.
///
/// The inner string is private and the only constructor is
/// [`S3ObjectPrefix::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// Every object this destination writes is keyed
/// `<prefix>/<account>/<backup id>.tar.gz`, and the prefix is the ONE part of
/// that an operator supplies — the rest is agent-derived from validated types.
/// An object key is not a path, but every tool an operator points at a bucket
/// renders it as one, which is why a `..` segment is refused: a key that reads
/// as `..` climbs out of the prefix an administrator meant to confine a panel
/// to, both in those tools and in any later sync onto a filesystem.
///
/// The empty prefix is a legal value, not a refusal: writing at the root of a
/// bucket dedicated to backups is an ordinary choice, and the key is then
/// `<account>/<backup id>.tar.gz` with no leading separator.
#[derive(Debug, Clone, Default, PartialEq, Eq, Hash)]
pub struct S3ObjectPrefix(String);

impl S3ObjectPrefix {
    /// Validates `candidate` as an object-key prefix and wraps it.
    ///
    /// # Errors
    ///
    /// - [`S3ObjectPrefixError::TooLong`] above 256 bytes.
    /// - [`S3ObjectPrefixError::LeadingSlash`] when it begins with `/`, which
    ///   would make the first segment of every key empty — one object with two
    ///   spellings.
    /// - [`S3ObjectPrefixError::IllegalCharacter`] for anything but `A-Z`,
    ///   `a-z`, `0-9`, `/`, `_` and `-`.
    /// - [`S3ObjectPrefixError::EmptySegment`] for `//` and for a trailing `/`,
    ///   for the same one-key-one-spelling reason.
    /// - [`S3ObjectPrefixError::TraversalSegment`] for a `..` segment.
    pub fn parse(candidate: &str) -> Result<Self, S3ObjectPrefixError> {
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(S3ObjectPrefixError::TooLong {
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        if candidate.is_empty() {
            return Ok(Self(String::new()));
        }

        if candidate.starts_with('/') {
            return Err(S3ObjectPrefixError::LeadingSlash);
        }

        for character in candidate.chars() {
            if !character.is_ascii_alphanumeric()
                && character != '/'
                && character != '_'
                && character != '-'
                && character != '.'
            {
                return Err(S3ObjectPrefixError::IllegalCharacter { character });
            }
        }

        for segment in candidate.split('/') {
            if segment.is_empty() {
                return Err(S3ObjectPrefixError::EmptySegment);
            }
            if segment == ".." || segment == "." {
                return Err(S3ObjectPrefixError::TraversalSegment);
            }
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated prefix, as it appears at the head of an object key.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// Whether this destination writes at the root of its bucket.
    ///
    /// Asked by the code that builds a key, so that the separator between the
    /// prefix and the account is written once and not by every caller deciding
    /// for itself whether the empty prefix needs one.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/s3_object_prefix_tests.rs"]
mod tests;
