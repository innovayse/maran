//! Why a candidate message size limit was refused.

/// Reasons [`super::mail_message_size_mib::MailMessageSizeMib::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailMessageSizeMibError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "no limit": an absent wire field decodes to
    /// zero, so accepting it would turn a caller that forgot the figure into a
    /// ceiling of nothing.
    #[error("a message size limit cannot be 0")]
    Zero,

    /// The candidate was above the largest figure accepted.
    #[error("`{value}` is above the largest accepted figure, {maximum}")]
    TooLarge {
        /// What was offered.
        value: u32,
        /// The largest figure accepted.
        maximum: u32,
    },
}
