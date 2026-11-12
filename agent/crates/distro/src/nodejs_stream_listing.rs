//! What a read of the `nodejs` module's streams found.

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;

/// The reading of one `module list nodejs` run on a family that ships Node.js as module streams
/// (issue #532).
///
/// Two different facts come out of one listing and are kept apart: which streams the sources
/// OFFER (a catalogue major with the `common` profile the panel installs), and which stream the
/// host has ENABLED (the `[e]` marker). A Node.js on the host with no enabled stream was not
/// installed through the module system, so the panel does not manage it.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct NodejsStreamListing {
    /// The catalogue's streams the sources list with the `common` profile, in listed order, once
    /// each.
    pub offered: Vec<NodejsModuleStream>,
    /// The stream names the host has enabled, as the digits the listing printed (a stream outside
    /// the catalogue included), in listed order.
    pub enabled: Vec<String>,
}
