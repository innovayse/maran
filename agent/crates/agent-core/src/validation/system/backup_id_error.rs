//! Why a backup id was refused.

/// Reasons [`super::backup_id::BackupId::parse`] refuses a candidate.
///
/// The id names an artifact and a sidecar under `/var/backups/maran`, a scratch
/// directory under the agent's own root-only scratch, two restore staging
/// directories, and an object key in a bucket — and it is the ONLY part of any
/// of those that does not come from a constant. That is why the grammar is as
/// narrow as it is and why every refusal below is about shape rather than about
/// meaning: a value that is exactly 36 characters of lowercase hex and hyphens
/// cannot hold a `/`, a `..`, a leading `/`, a NUL or a newline, so the path
/// helpers need no traversal check of their own — there is nothing left to
/// traverse with.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BackupIdError {
    /// The candidate was not exactly 36 bytes.
    ///
    /// Checked first and by length rather than by shape, because it is the
    /// refusal that makes every position below meaningful. An empty id would
    /// otherwise name the account's backup directory itself, and a delete of it
    /// would be a delete of every backup that account has.
    ///
    /// Both numbers are BYTES, and they are bytes because the comparison is:
    /// reporting a character count next to a byte comparison produces "is
    /// exactly 36 bytes, not 36" for a 37-byte candidate holding one multi-byte
    /// character. Every character this type accepts is ASCII, so for anything
    /// that could have been valid the two counts are the same number anyway.
    #[error("a backup id is exactly {expected} bytes, not {actual}")]
    WrongLength {
        /// The length, in bytes, a hyphenated uuid has.
        expected: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// A hyphen was missing from, or present outside, the four fixed positions.
    #[error("a backup id has hyphens only at positions 8, 13, 18 and 23")]
    MisplacedHyphen {
        /// Where the shape first disagreed with a uuid.
        position: usize,
    },

    /// A character outside `0-9` and `a-f` was found.
    ///
    /// Uppercase hex is refused with everything else, and deliberately. A
    /// filesystem that distinguishes case would give `A1B2…` and `a1b2…` two
    /// different artifacts for what the panel believes is one backup, and one
    /// that does not would give them one artifact and two backups — so a
    /// retention pass would delete a file a second row still claims. An object
    /// store distinguishes case always, so the same id would name two objects
    /// there whatever the local filesystem did.
    #[error("a backup id holds only lowercase hexadecimal digits, not `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
