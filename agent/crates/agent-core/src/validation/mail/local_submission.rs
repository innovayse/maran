//! The binding of an account's locally submitted mail to its own sender
//! addresses and send limit.

use std::collections::HashSet;

use super::local_submission_error::LocalSubmissionError;
use super::mail_address::MailAddress;
use super::mail_domain::MailDomain;
use super::send_limit_per_hour::SendLimitPerHour;
use crate::validation::system::name::AccountName;

/// The lowest system user id a local submitter may have: the floor every
/// supported distribution gives its human and hosting accounts. Root and the
/// system accounts are below it.
pub const MINIMUM_UID: u32 = 1000;

/// The highest system user id a local submitter may have: `useradd`'s default
/// `UID_MAX`. `nobody` (65534) and the unmapped id are above it.
pub const MAXIMUM_UID: u32 = 60_000;

/// The most sender domains one account may have.
pub const MAXIMUM_DOMAINS: usize = 1_000;

/// The most mailbox logins one account may have.
pub const MAXIMUM_MAILBOX_LOGINS: usize = 10_000;

/// What the mail filter needs to bind one account's local submission: who the
/// system user is, which domains it may send from, how many messages an hour it
/// may send and which mailbox logins share that hourly count.
///
/// Built only by [`LocalSubmission::parse`], which is the single gate: every
/// field is validated there and the values are written into line-oriented map
/// files, so a separator in any of them would change what a line means
/// (rules/security.md item 4). The fields are public for the renderer to read;
/// a value built by hand skips the gate and is a defect.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LocalSubmission {
    /// The account the binding belongs to.
    pub account: AccountName,
    /// The account's numeric system user id, in `MINIMUM_UID..=MAXIMUM_UID`.
    pub uid: u32,
    /// Every domain the account may send from, lower-cased, without repeats, in
    /// the order given.
    pub domains: Vec<MailDomain>,
    /// Messages per hour, local and authenticated together.
    pub send_limit_per_hour: SendLimitPerHour,
    /// The login of every mailbox of the account, without repeats, in the order
    /// given.
    pub mailbox_logins: Vec<MailAddress>,
}

impl LocalSubmission {
    /// Validates every part and assembles the binding.
    ///
    /// The list lengths are checked BEFORE any element is parsed, so an
    /// enormous list costs nothing.
    ///
    /// # Errors
    ///
    /// - [`LocalSubmissionError::Account`] for a refused account name.
    /// - [`LocalSubmissionError::UidOutOfRange`] for an id outside
    ///   [`MINIMUM_UID`]`..=`[`MAXIMUM_UID`].
    /// - [`LocalSubmissionError::TooManyDomains`],
    ///   [`LocalSubmissionError::InvalidDomain`] and
    ///   [`LocalSubmissionError::DuplicateDomain`] for the domain list.
    /// - [`LocalSubmissionError::SendLimit`] for a refused figure (zero included).
    /// - [`LocalSubmissionError::TooManyMailboxLogins`],
    ///   [`LocalSubmissionError::InvalidMailboxLogin`] and
    ///   [`LocalSubmissionError::DuplicateMailboxLogin`] for the login list.
    pub fn parse<D: AsRef<str>, L: AsRef<str>>(
        account: &str,
        uid: u32,
        domains: &[D],
        send_limit_per_hour: u32,
        mailbox_logins: &[L],
    ) -> Result<Self, LocalSubmissionError> {
        let account = AccountName::parse(account)?;

        if !(MINIMUM_UID..=MAXIMUM_UID).contains(&uid) {
            return Err(LocalSubmissionError::UidOutOfRange {
                minimum: MINIMUM_UID,
                maximum: MAXIMUM_UID,
            });
        }

        if domains.len() > MAXIMUM_DOMAINS {
            return Err(LocalSubmissionError::TooManyDomains {
                maximum: MAXIMUM_DOMAINS,
            });
        }

        let mut seen_domains = HashSet::new();
        let mut parsed_domains = Vec::with_capacity(domains.len());
        for (index, candidate) in domains.iter().enumerate() {
            let domain = MailDomain::parse(candidate.as_ref())
                .map_err(|source| LocalSubmissionError::InvalidDomain { index, source })?;
            if !seen_domains.insert(domain.clone()) {
                return Err(LocalSubmissionError::DuplicateDomain { index });
            }
            parsed_domains.push(domain);
        }

        let send_limit_per_hour = SendLimitPerHour::parse(send_limit_per_hour)?;

        if mailbox_logins.len() > MAXIMUM_MAILBOX_LOGINS {
            return Err(LocalSubmissionError::TooManyMailboxLogins {
                maximum: MAXIMUM_MAILBOX_LOGINS,
            });
        }

        let mut seen_logins = HashSet::new();
        let mut parsed_logins = Vec::with_capacity(mailbox_logins.len());
        for (index, candidate) in mailbox_logins.iter().enumerate() {
            let login = MailAddress::parse(candidate.as_ref())
                .map_err(|source| LocalSubmissionError::InvalidMailboxLogin { index, source })?;
            if !seen_logins.insert(login.clone()) {
                return Err(LocalSubmissionError::DuplicateMailboxLogin { index });
            }
            parsed_logins.push(login);
        }

        Ok(Self {
            account,
            uid,
            domains: parsed_domains,
            send_limit_per_hour,
            mailbox_logins: parsed_logins,
        })
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/local_submission_tests.rs"]
mod tests;
