//! The four host daemons the mail stack is made of.

/// One daemon the mail lifecycle starts, stops and observes.
///
/// A closed set: the lifecycle never starts anything that is not on this list,
/// and the order of [`Self::START_ORDER`] is the order a start needs — what a
/// daemon talks to is up before the daemon that talks to it.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum MailDaemon {
    /// The Redis-protocol server (Redis or Valkey) Rspamd keeps counters in.
    KeyValueServer,
    /// The spam filter, DKIM signer and milter.
    Rspamd,
    /// IMAP, POP3, LMTP delivery and SASL.
    Dovecot,
    /// The SMTP server.
    Postfix,
}

impl MailDaemon {
    /// Every daemon, in the order a start needs: the key-value server before the
    /// filter that uses it, the filter and Dovecot before the Postfix that
    /// delivers to the one and filters through the other.
    pub const START_ORDER: [Self; 4] = [
        Self::KeyValueServer,
        Self::Rspamd,
        Self::Dovecot,
        Self::Postfix,
    ];
}
