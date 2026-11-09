//! Why an archive key was refused.

/// Rejection reasons for [`super::archive_key::ArchiveKey::parse`].
///
/// No variant carries the key or any part of it, not even the position of the
/// offending character: an error enum is what reaches an operator log, and a
/// rejected key is still a secret (rules/security.md item 8).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ArchiveKeyError {
    /// The candidate is not 64 characters long.
    #[error("the archive key is not 64 characters")]
    WrongLength,
    /// The candidate holds a character that is not a lowercase hexadecimal digit.
    #[error("the archive key is not lowercase hexadecimal")]
    NotLowercaseHex,
}
