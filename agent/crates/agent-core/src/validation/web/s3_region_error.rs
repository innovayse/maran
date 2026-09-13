//! Why an S3 region was refused.

/// Reasons [`super::s3_region::S3Region::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum S3RegionError {
    /// The candidate was empty.
    ///
    /// A store that needs no meaningful region still needs a word for it —
    /// `auto` and `us-east-1` are both such words — so the empty string is a
    /// destination nobody finished configuring rather than a default.
    #[error("a region cannot be empty")]
    Empty,

    /// The candidate was longer than a region may be.
    #[error("a region is at most {maximum} bytes, not {actual}")]
    TooLong {
        /// The most bytes a region may have.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// A character outside `a-z`, `0-9` and `-` was found.
    ///
    /// Uppercase is refused with everything else: the region is part of the
    /// scope string a request signature is computed over, which is compared
    /// byte for byte, so two spellings of one region are two signatures and one
    /// of them is rejected by the store with no explanation the operator can
    /// act on.
    #[error("a region holds only lowercase letters, digits and `-`, not `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
