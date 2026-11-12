//! The refresh of the two address sets of the output chain, rendered alone.

use std::net::{Ipv4Addr, Ipv6Addr};

use askama::Template;

use crate::render_error::RenderError;

/// Renders the `nft -f` script that replaces the contents of `self_v4` and
/// `self_v6` in `table inet maran` and touches nothing else.
///
/// The sets are filled from the host's addresses, which change without anybody
/// editing a rule (a lease, a new interface, a privacy address); the script is how
/// the kernel follows them without re-applying the whole ruleset. Each set is
/// flushed and refilled inside one `nft -f` transaction, so a connection is never
/// judged against an empty set. The fields are the ones of
/// [`NftablesRuleset`](crate::nftables::nftables_ruleset::NftablesRuleset) and
/// carry the same promise: no loopback, nothing inside anything else.
#[derive(Template)]
#[template(path = "nftables/self_sets.nft.j2", escape = "none")]
pub struct NftablesSelfSets {
    /// The local IPv4 ranges, as the first address and the prefix length.
    pub self_v4_prefixes: Vec<(Ipv4Addr, u8)>,
    /// The host's own IPv4 addresses.
    pub self_v4: Vec<Ipv4Addr>,
    /// The host's own IPv6 addresses.
    pub self_v6: Vec<Ipv6Addr>,
}

impl NftablesSelfSets {
    /// Renders the script.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
