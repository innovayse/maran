//! A site's IP allow or deny list.

use maran_agent_core::validation::web::source_cidr::SourceCidr;

use crate::sites::model::ip_access_mode::IpAccessMode;
use crate::sites::model::site_options_error::SiteOptionsError;

/// The most networks one site's IP list may hold.
pub const MAXIMUM_NETWORKS: usize = 25;

/// A mode and a non-empty, duplicate-free list of validated networks.
///
/// Fields are private and the only constructor is [`IpAccess::new`]. "No IP
/// rule" is the absence of this value, never an empty one.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IpAccess {
    /// Whether the networks are the ones allowed or the ones refused.
    mode: IpAccessMode,
    /// The networks, in the order the owner gave them.
    networks: Vec<SourceCidr>,
}

impl IpAccess {
    /// Builds an IP list.
    ///
    /// # Errors
    ///
    /// - [`SiteOptionsError::EmptyIpList`] when `networks` is empty.
    /// - [`SiteOptionsError::TooManyNetworks`] beyond [`MAXIMUM_NETWORKS`].
    /// - [`SiteOptionsError::DuplicateNetwork`] when a network appears twice.
    pub fn new(mode: IpAccessMode, networks: Vec<SourceCidr>) -> Result<Self, SiteOptionsError> {
        if networks.is_empty() {
            return Err(SiteOptionsError::EmptyIpList);
        }
        if networks.len() > MAXIMUM_NETWORKS {
            return Err(SiteOptionsError::TooManyNetworks {
                count: networks.len(),
                maximum: MAXIMUM_NETWORKS,
            });
        }
        if networks
            .iter()
            .enumerate()
            .any(|(index, network)| networks[..index].contains(network))
        {
            return Err(SiteOptionsError::DuplicateNetwork);
        }

        Ok(Self { mode, networks })
    }

    /// Whether the networks are the ones allowed or the ones refused.
    #[must_use]
    pub fn mode(&self) -> IpAccessMode {
        self.mode
    }

    /// The networks, in the order the owner gave them.
    #[must_use]
    pub fn networks(&self) -> &[SourceCidr] {
        &self.networks
    }
}

#[cfg(test)]
#[path = "../../tests/sites/model/ip_access_tests.rs"]
mod tests;
