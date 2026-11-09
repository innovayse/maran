//! A zone the agent keeps a file for, and whether the daemon serves it.

use maran_agent_core::validation::dns::zone_name::ZoneName;

/// One entry of the zone directory read back, with what the daemon says about it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ListedZone {
    /// The zone the file belongs to.
    pub zone: ZoneName,
    /// Whether the daemon currently serves it.
    pub served: bool,
}
