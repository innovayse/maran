//! The bucket an S3-compatible destination writes its objects into.

use super::s3_bucket_error::S3BucketError;

/// The fewest characters a bucket name may have.
const MINIMUM_LENGTH: usize = 3;

/// The most characters a bucket name may have.
const MAXIMUM_LENGTH: usize = 63;

/// A validated S3 bucket name.
///
/// The inner string is private and the only constructor is
/// [`S3Bucket::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// The value is interpolated into a request URL — as a host label for
/// virtual-hosted addressing, or as the first path segment for path-style — so
/// it is a value that decides which server the agent talks to and which
/// container it writes a customer's database into. The grammar below is the
/// DNS-label grammar buckets are addressed by, which is narrower than what an
/// object store might technically accept: a name that cannot appear in a host
/// label cannot smuggle a `/`, a `@`, a `:` or a `?` into either position.
///
/// Refused, never repaired. Lowercasing a name for the operator would mean the
/// bucket they typed and the bucket the agent writes to are not certainly the
/// same bucket, and "certainly the same" is the only useful property here.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct S3Bucket(String);

impl S3Bucket {
    /// Validates `candidate` as a bucket name and wraps it.
    ///
    /// # Errors
    ///
    /// - [`S3BucketError::WrongLength`] outside 3..=63 bytes, which is what
    ///   refuses the empty name.
    /// - [`S3BucketError::IllegalCharacter`] for anything but `a-z`, `0-9`, `-`
    ///   and `.` — uppercase letters and `_` included.
    /// - [`S3BucketError::EdgeNotAlphanumeric`] when the first or last
    ///   character is not a letter or a digit.
    /// - [`S3BucketError::EmptyLabel`] for two adjacent dots, which is a label
    ///   of length zero and not a name any addressing style can carry.
    /// - [`S3BucketError::LooksLikeIpAddress`] for four dot-separated numbers,
    ///   which an object store cannot distinguish from the address of a host.
    pub fn parse(candidate: &str) -> Result<Self, S3BucketError> {
        if candidate.len() < MINIMUM_LENGTH || candidate.len() > MAXIMUM_LENGTH {
            return Err(S3BucketError::WrongLength {
                minimum: MINIMUM_LENGTH,
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        for character in candidate.chars() {
            if !character.is_ascii_lowercase()
                && !character.is_ascii_digit()
                && character != '-'
                && character != '.'
            {
                return Err(S3BucketError::IllegalCharacter { character });
            }
        }

        // Checked on bytes: every character that survived the loop above is
        // ASCII, so the first and last byte are the first and last character.
        let bytes = candidate.as_bytes();
        let edges_are_alphanumeric = bytes.first().is_some_and(u8::is_ascii_alphanumeric)
            && bytes.last().is_some_and(u8::is_ascii_alphanumeric);
        if !edges_are_alphanumeric {
            return Err(S3BucketError::EdgeNotAlphanumeric);
        }

        if candidate.split('.').any(str::is_empty) {
            return Err(S3BucketError::EmptyLabel);
        }

        if looks_like_ip_address(candidate) {
            return Err(S3BucketError::LooksLikeIpAddress);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated bucket name, as it appears in a request.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

/// Whether `candidate` is four dot-separated groups of decimal digits.
///
/// Deliberately looser than parsing an address: `999.999.999.999` is not an
/// address and is still refused, because what matters is whether the name is
/// AMBIGUOUS with one when it appears where a host may — not whether it would
/// route.
fn looks_like_ip_address(candidate: &str) -> bool {
    let mut labels = 0;
    for label in candidate.split('.') {
        if label.is_empty() || !label.bytes().all(|byte| byte.is_ascii_digit()) {
            return false;
        }
        labels += 1;
    }

    labels == 4
}

#[cfg(test)]
#[path = "../../tests/validation/web/s3_bucket_tests.rs"]
mod tests;
