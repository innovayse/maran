//! What the host says about the mail stack beyond which daemons run and where.

use crate::mail::model::tool_version::ToolVersion;

/// The extra readings the mail status reports, each asked of the host.
///
/// Every field is `None` when the reading was not made or did not answer, which
/// is a different fact from a negative answer: `Some(false)` means the host was
/// asked and said no, `None` means nothing is known and a caller must not
/// certify either way.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MailDetails {
    /// The MTA's version (`postconf -h mail_version`).
    pub postfix_version: Option<ToolVersion>,
    /// The IMAP server's version (`doveadm --version`).
    pub dovecot_version: Option<ToolVersion>,
    /// The spam filter's version (`rspamadm --version`).
    pub rspamd_version: Option<ToolVersion>,
    /// Whether the running IMAP server lists the host's password scheme among the
    /// schemes it can verify (`doveadm pw -l`).
    pub password_scheme_listed: Option<bool>,
    /// How many messages the MTA's queues hold (`postqueue -j`, one line per
    /// message). `None` when the MTA is not running or the listing outran its
    /// bound; an empty queue is `Some(0)`.
    pub queue_messages: Option<u32>,
    /// Whether the IMAP server's command line knows the Sieve commands.
    pub sieve_available: Option<bool>,
}
