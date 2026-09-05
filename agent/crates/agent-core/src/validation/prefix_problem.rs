//! Why a requested name could not be prefixed.

/// Why a requested name could not be prefixed. The public types map each case
/// onto their own domain error, so this stays crate-internal vocabulary.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum PrefixProblem {
    /// Nothing was requested.
    Empty,
    /// A character outside `[a-z0-9]` — the separator included, which is what
    /// stops account `alice` requesting `bob_admin` and being handed a name
    /// that reads as `bob`'s.
    UnexpectedCharacter {
        /// The offending character.
        character: char,
    },
    /// The prefixed result exceeds the caller's limit.
    TooLong {
        /// The length the prefixed result would have had.
        length: usize,
    },
}
