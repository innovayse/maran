//! The region an S3-compatible destination is addressed in.

use super::s3_region_error::S3RegionError;

/// The most characters a region may have.
const MAXIMUM_LENGTH: usize = 32;

/// A validated S3 region.
///
/// The inner string is private and the only constructor is
/// [`S3Region::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// The value reaches two places, and the second is why the grammar is this
/// narrow: an endpoint host, and the **scope string of a request signature**.
/// A region carrying a `/` would end a scope segment early, which is a
/// signature computed over something other than what the operator configured.
/// Lowercase letters, digits and hyphens carry no separator any of those
/// grammars reads.
///
/// No list of known regions lives here. The panel talks to S3-compatible stores
/// whose region names it has never heard of — `auto`, `fra1`, a private
/// deployment's own word — and a validator that refused those would be refusing
/// working configurations to look thorough.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct S3Region(String);

impl S3Region {
    /// Validates `candidate` as a region and wraps it.
    ///
    /// # Errors
    ///
    /// - [`S3RegionError::Empty`] when `candidate` is empty.
    /// - [`S3RegionError::TooLong`] above 32 bytes.
    /// - [`S3RegionError::IllegalCharacter`] for anything but `a-z`, `0-9` and
    ///   `-` — uppercase included, because a region appears in a signature
    ///   scope that is compared byte for byte.
    pub fn parse(candidate: &str) -> Result<Self, S3RegionError> {
        if candidate.is_empty() {
            return Err(S3RegionError::Empty);
        }

        if candidate.len() > MAXIMUM_LENGTH {
            return Err(S3RegionError::TooLong {
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        for character in candidate.chars() {
            if !character.is_ascii_lowercase() && !character.is_ascii_digit() && character != '-' {
                return Err(S3RegionError::IllegalCharacter { character });
            }
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated region, as it appears in an endpoint and a signature.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/s3_region_tests.rs"]
mod tests;
