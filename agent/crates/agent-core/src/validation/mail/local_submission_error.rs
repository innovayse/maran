//! Why a local submission binding was refused.

use super::alias_fanout_error::AliasFanoutError;
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

    /// More alias fan-outs than the ceiling.
    #[error("an account cannot have more than {maximum} alias fan-outs")]
    TooManyAliasFanouts {
        /// The most fan-outs accepted.
        maximum: usize,
    },

    /// An alias fan-out is not an acceptable alias and count.
    #[error("alias fan-out {index} is not valid: {source}")]
    InvalidAliasFanout {
        /// The zero-based position of the refused fan-out.
        index: usize,
        /// Why it was refused.
        source: AliasFanoutError,
    },

    /// An alias fan-out names an address outside the account's sender domains.
    ///
    /// The count is charged to the account that sends to the alias, and the
    /// alias key is shared by every account, so an alias the account does not
    /// own must never be written under its binding.
    #[error("alias fan-out {index} is not on one of the account's domains")]
    AliasFanoutOutsideDomains {
        /// The zero-based position of the refused fan-out.
        index: usize,
    },

    /// Two alias fan-outs are the same address once lower-cased.
    #[error("alias fan-out {index} repeats an earlier one")]
    DuplicateAliasFanout {
        /// The zero-based position of the repeat.
        index: usize,
    },
}
