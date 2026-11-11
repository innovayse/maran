//! The keys of the local-submission tables that belong to one account at a time.

use std::fmt;

/// A value that may be bound to ONE account (docs/mail/local-submission.md,
/// "One owner per key").
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum LocalSubmissionKey {
    /// A mailbox login, which names the account whose send limit it spends.
    MailboxLogin,
    /// A sender domain, which names the account allowed to send as it.
    SenderDomain,
}

impl fmt::Display for LocalSubmissionKey {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(match self {
            Self::MailboxLogin => "mailbox login",
            Self::SenderDomain => "sender domain",
        })
    }
}
