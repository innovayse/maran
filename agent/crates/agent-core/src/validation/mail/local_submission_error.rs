//! Why a local submission binding was refused.

use super::mail_address_error::MailAddressError;
use super::mail_domain_error::MailDomainError;
use super::send_limit_per_hour_error::SendLimitPerHourError;
use crate::validation::system::name_error::NameError;

/// Reasons [`super::local_submission::LocalSubmission::parse`] refuses a binding.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum LocalSubmissionError {
    /// The account name is not an acceptable account name.
    #[error("account: {0}")]
    Account(#[from] NameError),

    /// The user id lies outside the range hosting accounts are given.
    ///
    /// Root, the system accounts and `nobody` sit outside it: the filter trusts
    /// the id alone, so an id outside the range must never become a sender.
    #[error("the system user id must be between {minimum} and {maximum}")]
    UidOutOfRange {
        /// The lowest id accepted.
        minimum: u32,
        /// The highest id accepted.
        maximum: u32,
    },

    /// More domains than the ceiling.
    #[error("an account cannot have more than {maximum} sender domains")]
    TooManyDomains {
        /// The most domains accepted.
        maximum: usize,
    },

    /// A domain is not an acceptable mail domain.
    #[error("domain {index} is not valid: {source}")]
    InvalidDomain {
        /// The zero-based position of the refused domain.
        index: usize,
        /// Why it was refused.
        source: MailDomainError,
    },

    /// Two domains are the same once lower-cased.
    #[error("domain {index} repeats an earlier one")]
    DuplicateDomain {
        /// The zero-based position of the repeat.
        index: usize,
    },

    /// The send limit is not an acceptable figure.
    #[error("send limit: {0}")]
    SendLimit(#[from] SendLimitPerHourError),

    /// More mailbox logins than the ceiling.
    #[error("an account cannot have more than {maximum} mailbox logins")]
    TooManyMailboxLogins {
        /// The most logins accepted.
        maximum: usize,
    },

    /// A mailbox login is not an acceptable address.
    #[error("mailbox login {index} is not valid: {source}")]
    InvalidMailboxLogin {
        /// The zero-based position of the refused login.
        index: usize,
        /// Why it was refused.
        source: MailAddressError,
    },

    /// Two mailbox logins are the same address once lower-cased.
    #[error("mailbox login {index} repeats an earlier one")]
    DuplicateMailboxLogin {
        /// The zero-based position of the repeat.
        index: usize,
    },
}
