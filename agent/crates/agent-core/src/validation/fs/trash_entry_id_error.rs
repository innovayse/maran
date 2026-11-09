//! Why a trash entry identifier was refused.

/// Rejection reasons for [`super::trash_entry_id::TrashEntryId::parse`].
///
/// The variants carry no fragment of the text that produced them.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum TrashEntryIdError {
    /// The text is not `<unix seconds>-<16 lower-case hex digits>`.
    #[error("trash entry id has the wrong shape")]
    Shape,
    /// The seconds part does not fit the clock the agent reads.
    #[error("trash entry id has an out-of-range time")]
    Time,
}
