//! One SSH port of the host, with whatever the operator has authored for it.

use crate::nftables::nftables_allow::NftablesAllow;

/// A port the host's sshd listens on, and the operator's own rules for it.
///
/// **A host can serve SSH on several ports at once.** sshd listens on every
/// `Port` directive and on every `ListenAddress host:port`, across the main
/// config and everything its `Include` pulls in — which on the Debian family is
/// where a port override usually lives. A ruleset rendered for one of them
/// opens that one and closes the rest, and which one it happened to be depends
/// on line order in a config file. So the caller passes them all, and the
/// rendered policy accepts the union.
///
/// The pairing is structural rather than conventional, and that is the point of
/// this type. The template renders [`Self::port`] on every line of this port's
/// block and never a rule's own port, so a rule that reached the wrong group
/// still renders as an accept for a port sshd is listening on — the fail-safe
/// direction. If the grouping were a `Vec<NftablesAllow>` the template had to
/// filter itself, a routing regression would render a rule for some other port
/// AND suppress this port's fallback, which is SSH closed with no remote
/// recovery.
pub struct NftablesSshPort {
    /// The port sshd listens on.
    pub port: u16,
    /// The operator's own TCP rules for THIS port.
    ///
    /// Empty is the ordinary case and renders the unconditional accept that
    /// stops an apply locking the operator out. One or more rules render
    /// INSTEAD of it, which is how an administrator source-restricts SSH; the
    /// caller puts a rule here only when its port is this one and its protocol
    /// is TCP, because a UDP rule for the same number taking the fallback's
    /// place would close the TCP port the operator is connected on.
    pub rules: Vec<NftablesAllow>,
}
