//! The firewall's whole policy: `table inet maran`, rendered as one file.

use std::net::{Ipv4Addr, Ipv6Addr};

use askama::Template;

use crate::nftables::nftables_allow::NftablesAllow;
use crate::nftables::nftables_ssh_port::NftablesSshPort;
use crate::render_error::RenderError;

/// Renders the agent's nftables rules table — the complete policy, as one file
/// that is applied with `nft -f`.
///
/// Two properties of the rendered text are security, not formatting, and a
/// change to either is a change to what the firewall does:
///
/// **The replace idiom.** The file opens with a no-op `table inet maran {}`, a
/// `delete table inet maran`, and only then the real declaration. `nft -f` is
/// ADDITIVE: without those three lines, re-applying a re-rendered file leaves
/// every removed rule live and duplicates the rest — a "deny" that reports
/// success while the port stays open. Create-if-absent, delete, redeclare is
/// what makes an apply CONVERGE on the rendered text.
///
/// **Loopback first.** `iif "lo" accept` precedes every drop, because the
/// panel's own web-server-to-application hop is loopback traffic and nothing —
/// not even a ban — may sever the panel from itself. The bans table repeats
/// the same exemption for the same reason: it hooks at a lower priority and so
/// runs BEFORE this chain (see
/// [`NftablesBansTable`](crate::nftables::nftables_bans_table::NftablesBansTable)).
///
/// **The output chain.** After the input chain, `table inet maran` holds an
/// `output` chain with `policy accept` and two rules: a uid at or above
/// [`Self::customer_uid_min`] connecting to port 25 of one of this host's own
/// addresses is reset. It is the second layer of the port-25 lockdown
/// (issue #531); the first is Postfix's own refusal of the host's addresses.
/// Stopping nftables loses only this layer. A reader of the file compares the
/// tail after the input chain with the current render, with the two
/// host-dependent values (the uid and the address list) masked.
///
/// Bans deliberately do not live in this table: `delete table` above would
/// erase them on every apply, so they get a table of their own.
#[derive(Template)]
// A config file is not a document: HTML-escaping a port number or a CIDR would
// corrupt the grammar `nft` parses. Values reaching a template are validated
// (rules/rust.md "Validation first"), which is what makes an escaper needless
// here rather than merely inconvenient.
#[template(path = "nftables/ruleset.nft.j2", escape = "none")]
pub struct NftablesRuleset {
    /// Every port the host's sshd listens on, each with the operator's own
    /// rules for it, and each always accepted one way or the other so that an
    /// apply cannot lock the operator out of the host.
    ///
    /// A LIST because sshd listens on every `Port` directive and on every
    /// `ListenAddress host:port` across the main config and its includes, so a
    /// host can legitimately serve SSH on several ports at once. Rendering one
    /// of them would open that one and close the rest — see
    /// [`NftablesSshPort`], which also carries why the per-port grouping is
    /// structural rather than a filter this template performs.
    ///
    /// It must not be empty: an empty list renders a policy-drop ruleset with
    /// no SSH accept at all. The caller validates that; there is nothing here
    /// that could.
    pub ssh_ports: Vec<NftablesSshPort>,
    /// The port the panel listens on, always accepted so an apply cannot lock
    /// the operator out of the panel.
    pub panel_port: u16,
    /// Every other rule the operator has added, rendered in the given order.
    pub allows: Vec<NftablesAllow>,
    /// The first uid of the customer range (`UID_MIN` of the host's
    /// `login.defs`): the output chain rejects port-25 connections to this
    /// host from every uid at or above it, and from no uid below it.
    pub customer_uid_min: u32,
    /// The IPv4 ranges the host's routing table marks local, as the first
    /// address and the prefix length, NOT counting loopback. None is inside
    /// another and no address of [`Self::self_v4`] is inside one: the set is a
    /// set of intervals and `nft` refuses two that overlap.
    pub self_v4_prefixes: Vec<(Ipv4Addr, u8)>,
    /// The host's own IPv4 addresses, NOT counting loopback.
    ///
    /// The template writes the loopback network itself as a constant, so a
    /// loopback address passed here would overlap it and `nft` would refuse
    /// the whole file. Typed `Ipv4Addr` so that only an address can reach a
    /// line-oriented file `nft -f` applies as root.
    pub self_v4: Vec<Ipv4Addr>,
    /// The host's own IPv6 addresses, NOT counting `::1`. See
    /// [`Self::self_v4`].
    pub self_v6: Vec<Ipv6Addr>,
}

impl NftablesRuleset {
    /// Renders the ruleset file.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
