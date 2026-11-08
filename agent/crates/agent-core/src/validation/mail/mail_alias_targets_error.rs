//! Why a set of alias targets was refused.

use super::mail_address_error::MailAddressError;

/// Reasons [`super::mail_alias_targets::MailAliasTargets::parse`] refuses a list.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailAliasTargetsError {
    /// The list was empty: an alias that goes nowhere is a delete, which has
    /// its own request.
    #[error("an alias needs at least one target")]
    Empty,

    /// More targets than the ceiling.
    #[error("an alias cannot have more than {maximum} targets")]
    TooMany {
        /// The most targets accepted.
        maximum: usize,
    },

    /// A target is not an acceptable address.
    #[error("target {index} is not valid: {source}")]
    InvalidTarget {
        /// The zero-based position of the refused target.
        index: usize,
        /// Why it was refused.
        source: MailAddressError,
    },

    /// Two targets are the same address once lower-cased.
    #[error("target {index} repeats an earlier target")]
    Duplicate {
        /// The zero-based position of the repeat.
        index: usize,
    },
}
