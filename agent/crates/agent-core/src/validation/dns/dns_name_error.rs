//! Why a candidate target name was refused.

/// Reasons [`super::dns_name::DnsName::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DnsNameError {
    /// The candidate was empty, or only a dot.
    #[error("a name cannot be empty")]
    Empty,

    /// Longer than 253 characters.
    #[error("a name cannot exceed 253 characters")]
    TooLong,

    /// A label was empty, over 63 characters, or began or ended with a hyphen.
    #[error("`{label}` is not a valid label")]
    InvalidLabel {
        /// The offending label, cut at 63 characters.
        label: String,
    },

    /// A character outside ASCII letters, digits, `-` and `_`.
    #[error("a name cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// A single label.
    #[error("a name needs at least two labels")]
    SingleLabel,

    /// The last label is all digits, so the name reads as an address.
    #[error("a name cannot end in a numeric label")]
    NumericFinalLabel,
}
