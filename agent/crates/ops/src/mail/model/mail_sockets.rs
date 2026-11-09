//! The unix sockets the mail daemons talk to each other over.

/// The four sockets that join Postfix, Dovecot and Rspamd.
///
/// **Deliberately not in `AgentPaths`, and the reason is a real distinction.**
/// That type is the set of locations the agent itself creates and writes, and
/// `maran structure` reads it that way: every absolute constant there outside
/// `/etc` must appear in the agent unit's `ReadWritePaths=`. The agent never opens
/// these. It renders the paths into the daemons' configuration, and each daemon,
/// under its own unit, creates and owns its socket (the same reasoning the FTPS
/// log path carries). They are also not platform facts, since both families get
/// the same names, so they are constants here beside the operations that render
/// them rather than adapter methods.
///
/// Sockets and not loopback ports, so no listening port exists for any of them.
/// The directories are the ones the packaged units create at start; that the
/// RHEL family's vendor Rspamd package creates `/run/rspamd` is UNVERIFIED, and
/// the lifecycle suite on that family owes the measurement.
pub struct MailSockets;

impl MailSockets {
    /// The socket Rspamd's milter worker listens on and Postfix connects to.
    pub const MILTER: &'static str = "/run/rspamd/maran-milter.sock";

    /// The socket Rspamd's controller worker listens on, mode 0600, so the
    /// packaged controller port is not bound.
    pub const CONTROLLER: &'static str = "/run/rspamd/maran-controller.sock";

    /// The socket Dovecot's LMTP service listens on and Postfix delivers to.
    pub const LMTP: &'static str = "/run/dovecot/maran-lmtp";

    /// The socket Dovecot's auth service listens on for Postfix's SASL.
    pub const SASL: &'static str = "/run/dovecot/maran-auth";
}
