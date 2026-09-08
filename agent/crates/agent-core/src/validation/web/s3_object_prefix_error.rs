//! Why an object-key prefix was refused.

/// Reasons [`super::s3_object_prefix::S3ObjectPrefix::parse`] refuses a
/// candidate.
///
/// The prefix is the only operator-supplied part of an object key; everything
/// after it comes from an [`crate::validation::system::name::AccountName`] and
/// a [`crate::validation::system::backup_id::BackupId`], both of which are
/// already grammars with nothing to attack. So this type is where the whole
/// key's shape is decided.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum S3ObjectPrefixError {
    /// The candidate was longer than a prefix may be.
    #[error("an object prefix is at most {maximum} bytes, not {actual}")]
    TooLong {
        /// The most bytes a prefix may have.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// The candidate began with `/`.
    ///
    /// A key is not a path and has no root, so a leading slash produces an
    /// empty first segment: `/maran/acme/…` and `maran/acme/…` would be two
    /// keys an operator reads as one object, and a listing under one of them
    /// would not find what a retention pass wrote under the other.
    #[error("an object prefix does not begin with `/` — a key has no root")]
    LeadingSlash,

    /// A character outside `A-Z`, `a-z`, `0-9`, `/`, `_`, `-` and `.` was
    /// found.
    ///
    /// Narrow on purpose. A key travels through a URL, a signature and an
    /// operator's own tooling, and the safe intersection of what those three
    /// agree about is small.
    #[error(
        "an object prefix holds only letters, digits, `/`, `_`, `-` and `.`, not `{character:?}`"
    )]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// Two separators met, or the prefix ended in one.
    ///
    /// Same reason as [`Self::LeadingSlash`]: the key that gets written and the
    /// key that gets listed must be spelled one way.
    #[error("an object prefix has no empty segment and no trailing `/`")]
    EmptySegment,

    /// A `.` or `..` segment was found.
    ///
    /// Refused rather than resolved. Nothing in this crate knows what a store
    /// will do with such a key, and everything that later renders keys as paths
    /// — an operator's sync, a mounted view, a restore run somewhere else —
    /// will climb out of the prefix the panel was confined to.
    #[error("an object prefix has no `.` or `..` segment")]
    TraversalSegment,
}
