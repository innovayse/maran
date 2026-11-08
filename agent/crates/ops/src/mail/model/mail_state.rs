//! What the host is actually doing about mail, as opposed to what it was asked to do.

use crate::mail::model::daemon_state::DaemonState;
use crate::mail::model::port_state::PortState;

/// The observed state of the mail stack.
///
/// Every field is the answer to a question asked of the MACHINE — the service
/// manager, the kernel's table of listening sockets, `postconf` reading the file
/// Postfix reads — and never a restatement of what the panel asked for. A status
/// that reported the panel's own intent would answer "enabled" for a stack whose
/// SMTP port is held by something else (rules/testing.md, "A check must be able to
/// observe what it reports on").
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MailState {
    /// The four daemons, in start order.
    pub daemons: Vec<DaemonState>,
    /// Each of the seven mail ports and how it is bound, in
    /// [`MailPorts::ALL`](crate::mail::model::mail_ports::MailPorts::ALL) order.
    pub ports: Vec<PortState>,
    /// The hostname Postfix is actually running with (`postconf -h myhostname`),
    /// or `None` when Postfix is not installed or did not answer.
    pub effective_hostname: Option<String>,
    /// Whether the live Postfix configuration names a certificate chain
    /// (`postconf -h smtpd_tls_chain_files` is not empty), or `None` when it did
    /// not answer.
    pub tls_configured: Option<bool>,
}
