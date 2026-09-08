//! Why an S3 bucket name was refused.

/// Reasons [`super::s3_bucket::S3Bucket::parse`] refuses a candidate.
///
/// The name reaches a request URL, either as a host label or as the first path
/// segment, so the grammar it is held to is the DNS-label grammar rather than
/// whatever a particular object store happens to tolerate. Every refusal below
/// is about that shape.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum S3BucketError {
    /// The candidate was outside the length a bucket name may have.
    ///
    /// The empty name lands here, which is what stops a destination saved with
    /// no bucket from addressing the store's root.
    #[error("a bucket name is {minimum} to {maximum} bytes, not {actual}")]
    WrongLength {
        /// The fewest bytes a bucket name may have.
        minimum: usize,
        /// The most bytes a bucket name may have.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// A character outside `a-z`, `0-9`, `-` and `.` was found.
    ///
    /// Uppercase letters are refused here rather than lowercased: a bucket name
    /// is a DNS label, the operator's text and the agent's request must name
    /// the same container, and repairing the name quietly makes those two
    /// different questions.
    #[error("a bucket name holds only lowercase letters, digits, `-` and `.`, not `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// The name began or ended with something other than a letter or a digit.
    #[error("a bucket name begins and ends with a letter or a digit")]
    EdgeNotAlphanumeric,

    /// Two dots met, so some label between them was empty.
    #[error("a bucket name has no empty label — `..` is not a bucket")]
    EmptyLabel,

    /// The name is four dot-separated numbers.
    ///
    /// Ambiguous with the address of a host in exactly the position the name is
    /// interpolated into, and an ambiguity in "which server is this" is not one
    /// to resolve by guessing.
    #[error("a bucket name may not be spelled like an IP address")]
    LooksLikeIpAddress,
}
