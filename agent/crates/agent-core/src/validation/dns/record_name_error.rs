//! Why a candidate record name was refused.

/// Reasons [`super::record_name::RecordName::parse`] and `fits_within` refuse a
/// candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RecordNameError {
    /// The candidate was empty.
    #[error("a record name cannot be empty")]
    Empty,

    /// A trailing dot: record names are relative to their zone.
    #[error("a record name is relative to its zone and cannot end in a dot")]
    Absolute,

    /// Longer than 253 characters.
    #[error("a record name cannot exceed 253 characters")]
    TooLong,

    /// The name, with its zone appended, exceeds 253 characters.
    #[error("a record name with its zone cannot exceed 253 characters")]
    TooLongInZone,

    /// A label was empty, over 63 characters, or began or ended with a hyphen.
    #[error("`{label}` is not a valid label")]
    InvalidLabel {
        /// The offending label, cut at 63 characters.
        label: String,
    },

    /// A character outside ASCII letters, digits, `-` and `_`.
    #[error("a record name cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// A `*` that is not alone as the leftmost label.
    #[error("a wildcard must be the whole leftmost label")]
    MisplacedWildcard,
}
