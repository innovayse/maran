//! The table of this host's own client addresses, refused on port 25.

use std::net::{Ipv4Addr, Ipv6Addr};

use askama::Template;

use crate::render_error::RenderError;

/// Renders the Postfix CIDR table that names every address a connection from
/// this host itself arrives from.
///
/// The loopback ranges are always in it; `ipv4` and `ipv6` are the addresses the
/// interfaces carry, already without the loopback ones (the caller drops them, so
/// a table never lists an address twice). Each maps to a `REJECT`, and the port 25
/// service looks a client up here before anything else (master.cf). The table is
/// rewritten whole each time the mail stages converge, so an address the host
/// gained is added and one it lost is gone.
#[derive(Template)]
#[template(path = "postfix/self_clients.cidr.j2", escape = "none")]
pub struct PostfixSelfClients {
    /// The IPv4 ranges the host's routing table marks local, each as its first
    /// address and prefix length (a routed block answers on this host as a whole),
    /// never the loopback range, which is written as a constant.
    pub ipv4_ranges: Vec<(Ipv4Addr, u8)>,
    /// The IPv4 addresses of the host's interfaces, each matched as a `/32`.
    pub ipv4: Vec<Ipv4Addr>,
    /// The IPv6 addresses of the host's interfaces, each matched as a `/128`.
    pub ipv6: Vec<Ipv6Addr>,
}

impl PostfixSelfClients {
    /// Renders the table's text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
