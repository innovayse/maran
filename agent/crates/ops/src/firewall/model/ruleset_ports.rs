//! The ports a rendered ruleset always accepts, whatever else it holds.

use maran_agent_core::validation::web::port::Port;

/// The host's SSH ports and its panel port, as the request that triggered a
/// firewall change reported them.
///
/// **All of them are host facts only the installer knows, and all arrive on
/// every mutation** (R2). None is a literal anywhere in this agent: a host
/// whose sshd listens on 2222 must not be locked out by a ruleset that only
/// knows 22, and the panel's port is nginx's public vhost port — emphatically
/// not the backend's own listen port, which is loopback-only and would render
/// a ruleset that closes the panel to the world under `policy drop`.
///
/// A struct rather than loose parameters, and that is the point of it
/// existing. Two arguments of one type, side by side, can be passed in the
/// wrong order and the compiler will not say a word — and the wrong order
/// here renders SSH's hard allow for the panel's port and the panel's for
/// SSH's, which locks the operator out of the host and the panel at once,
/// with no remote recovery. Named fields make the swap impossible to write.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RulesetPorts {
    /// Every port the host's sshd listens on.
    ///
    /// A LIST, and not because a host might be unusual: sshd listens on EVERY
    /// `Port` directive and on every `ListenAddress host:port`, across the
    /// main config and everything its `Include` pulls in — which on the Debian
    /// family is exactly where a port override is written. A single port on
    /// the wire opens one and closes the others, and which one it happened to
    /// be depends on line order in a config file. Accepting the union is the
    /// only direction that cannot lock somebody out.
    ///
    /// Each port is accepted unconditionally while no operator-authored TCP
    /// rule for THAT port exists; the moment one does, that rule renders
    /// instead and that port's unconditional accept disappears — the others
    /// are untouched. A UDP rule for the same number never displaces a
    /// fallback: SSH is TCP, and a UDP rule taking one's place would close the
    /// port the operator is connected on.
    ///
    /// It must not be empty. An empty list renders a `policy drop` ruleset
    /// with no SSH accept in it at all, which is the lockout this whole type
    /// exists to prevent — so the layer that builds one refuses an empty list
    /// rather than defaulting it to 22. A default would be a guess about the
    /// one fact this agent is not allowed to guess.
    pub ssh_ports: Vec<Port>,
    /// The public port the panel is reachable on.
    ///
    /// Accepted unconditionally, with no override in v1: a panel lockout has
    /// no remote recovery path at all. Singular, because the panel's vhost is
    /// written by the installer and listens on exactly one port.
    pub panel_port: Port,
}
