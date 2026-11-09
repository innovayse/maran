//! The ports the mail stack listens on once it is enabled.

/// The standard ports of the mail protocols, and which of them are always served.
///
/// Constants of the protocols and not settings: an operator opens exactly these in
/// the firewall, which is why the panel reports them and the agent opens none.
pub struct MailPorts;

impl MailPorts {
    /// Served whenever mail is enabled: SMTP, SMTPS, submission, IMAP, IMAPS.
    pub const ALWAYS: [u16; 5] = [25, 465, 587, 143, 993];

    /// The ports Postfix serves: SMTP, SMTPS and submission.
    pub const POSTFIX: [u16; 3] = [25, 465, 587];

    /// The ports Dovecot serves when POP3 is off: IMAP and IMAPS.
    pub const DOVECOT: [u16; 2] = [143, 993];

    /// Served only when POP3 is switched on: POP3 and POP3S.
    pub const POP3: [u16; 2] = [110, 995];

    /// Every port the status reports on, whether or not it is meant to be bound.
    pub const ALL: [u16; 7] = [25, 465, 587, 143, 993, 110, 995];

    /// The ports a given configuration must have bound once it is up.
    #[must_use]
    pub fn expected(pop3_enabled: bool) -> Vec<u16> {
        let mut ports = Self::ALWAYS.to_vec();
        if pop3_enabled {
            ports.extend(Self::POP3);
        }
        ports
    }
}
