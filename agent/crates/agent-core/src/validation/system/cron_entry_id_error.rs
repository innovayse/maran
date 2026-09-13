//! Why a cron entry id was refused.

/// Reasons [`super::cron_entry_id::CronEntryId::parse`] refuses a candidate.
///
/// The id names three files under an account's home, and it is the ONLY part of
/// those paths that does not come from a constant. That is why the grammar is
/// as narrow as it is and why every refusal below is about shape rather than
/// about meaning: a value that is exactly 36 characters of lowercase hex and
/// hyphens cannot hold a `/`, a `..`, a leading `/`, a NUL or a newline, so the
/// path helpers need no traversal check of their own — there is nothing left to
/// traverse with.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CronEntryIdError {
    /// The candidate was not exactly 36 bytes.
    ///
    /// Checked first and by length rather than by shape, because it is the
    /// refusal that makes every position below meaningful. An empty id would
    /// otherwise name the cron directory itself.
    ///
    /// Both numbers are BYTES, and they are bytes because the comparison is:
    /// reporting a character count next to a byte comparison produced "is
    /// exactly 36 bytes, not 36" for a 37-byte candidate holding one multi-byte
    /// character. Every character this type accepts is ASCII, so for anything
    /// that could have been valid the two counts are the same number anyway.
    #[error("a cron entry id is exactly {expected} bytes, not {actual}")]
    WrongLength {
        /// The length, in bytes, a hyphenated uuid has.
        expected: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// A hyphen was missing from, or present outside, the four fixed positions.
    #[error("a cron entry id has hyphens only at positions 8, 13, 18 and 23")]
    MisplacedHyphen {
        /// Where the shape first disagreed with a uuid.
        position: usize,
    },

    /// A character outside `0-9` and `a-f` was found.
    ///
    /// Uppercase hex is refused with everything else, and deliberately: a
    /// filesystem that distinguishes case would give `A1B2…` and `a1b2…` two
    /// different `.cmd` files for what the panel believes is one entry, and a
    /// filesystem that does not would give them one file and two entries.
    /// Neither is a state the agent should be able to reach.
    #[error("a cron entry id holds only lowercase hexadecimal digits, not `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
