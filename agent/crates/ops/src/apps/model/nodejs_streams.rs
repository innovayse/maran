//! The outcome of asking for a module-stream family's Node.js streams.

use maran_distro::NodejsStreamListing;

use crate::apps::model::nodejs_unavailable::NodejsUnavailable;

/// The streams of the `nodejs` module as the host listed them, or the reason there is no list.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NodejsStreams {
    /// The listing was read.
    Listed(NodejsStreamListing),
    /// The listing gave no answer; see [`NodejsUnavailable`].
    Unavailable(NodejsUnavailable),
}
