//! What the host is actually doing about DNS, as opposed to what it was asked to.

use crate::dns::model::dns_policy_observation::DnsPolicyObservation;

/// An observation of the DNS daemon, made by asking the host.
///
/// Every field is observed and none is the panel's record of what it requested
/// (rules/rust.md "reading a live config back"): the answer is the host's.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DnsState {
    /// The service manager reports the unit active.
    pub running: bool,
    /// The daemon answered a status request on its control channel. Stronger than
    /// `running`: a `Type=simple` unit is active as soon as the process has been
    /// forked, including over a daemon that then failed to load its configuration.
    pub control_channel_answered: bool,
    /// A TCP connection to port 53 on the loopback address was accepted.
    pub port_53_answering: bool,
    /// Whether the live configuration refuses recursion, resolving the LAST
    /// occurrence of the statement.
    pub recursion_refused: DnsPolicyObservation,
    /// Whether the live configuration rate-limits responses.
    pub response_rate_limiting: DnsPolicyObservation,
    /// The service manager reports the unit enabled for boot.
    pub enabled_at_boot: bool,
}
