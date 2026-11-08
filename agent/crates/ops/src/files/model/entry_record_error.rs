//! Why bytes from the dropped child were refused.

/// Rejection reasons of the record codec. Payload-free: the bytes are a
/// customer-steered child's, and none of them is worth echoing.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum EntryRecordError {
    /// The bytes ended before the record did.
    #[error("record is truncated")]
    Truncated,
    /// The record announced more entries than any answer may hold.
    #[error("record announces too many entries")]
    TooManyEntries,
    /// A name was longer than a filesystem allows.
    #[error("record name is too long")]
    NameTooLong,
    /// A name was not valid UTF-8.
    #[error("record name is not UTF-8")]
    NotUtf8,
    /// A kind or answer tag was not one this reader knows.
    #[error("record holds an unknown tag")]
    UnknownTag,
    /// Bytes were left after the record ended.
    #[error("record has trailing bytes")]
    TrailingBytes,
}
