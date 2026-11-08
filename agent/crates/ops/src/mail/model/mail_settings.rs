//! What the panel decides about the mail stack.

use maran_agent_core::validation::mail::mail_message_size_mib::MailMessageSizeMib;
use maran_agent_core::validation::mail::mail_server_hostname::MailServerHostname;
use maran_agent_core::validation::mail::spam_thresholds::SpamThresholds;

/// The input of [`enable_mail`](crate::mail::enable_mail): everything about the
/// stack the panel decides.
///
/// Every field is an already-validated type, so nothing here can carry a newline
/// into a line-oriented configuration file (rules/security.md item 4) and the
/// spam scores cannot reach the filter out of order. What is deliberately not
/// here: the certificate paths, which are derived from `hostname` inside the
/// agent's own store; every socket and map path, which the agent decides; and the
/// listening ports, which are the protocols' standard ones and not negotiable.
#[derive(Debug, Clone)]
pub struct MailSettings {
    /// The name the server introduces itself with, and the name its certificate
    /// material is filed under.
    pub hostname: MailServerHostname,
    /// Whether POP3 is served beside IMAP.
    pub pop3_enabled: bool,
    /// The largest message accepted.
    pub message_size: MailMessageSizeMib,
    /// The spam filter's three scores.
    pub spam: SpamThresholds,
}

impl MailSettings {
    /// The most recipients one message may name.
    pub const RECIPIENT_LIMIT: u32 = 100;

    /// The largest message accepted, in bytes.
    #[must_use]
    pub fn message_size_bytes(&self) -> u64 {
        u64::from(self.message_size.value()) * 1024 * 1024
    }
}

#[cfg(test)]
#[path = "../../tests/mail/mail_settings_tests.rs"]
mod tests;
