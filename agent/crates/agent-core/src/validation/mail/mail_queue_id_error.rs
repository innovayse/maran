//! Why a candidate queue id was refused.

/// Reasons [`super::mail_queue_id::MailQueueId::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailQueueIdError {
    /// Shorter or longer than any id the MTA generates.
    #[error("a queue id is between {minimum} and {maximum} characters")]
    WrongLength {
        /// The shortest accepted.
        minimum: usize,
        /// The longest accepted.
        maximum: usize,
    },

    /// A character other than an ASCII letter or digit.
    #[error("a queue id cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
