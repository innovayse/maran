//! Whether the Node.js on a module-stream host came through a module stream.

use maran_distro::DistroAdapter;

use crate::apps::model::nodejs_streams::NodejsStreams;
use crate::apps::offered_nodejs_streams::offered_nodejs_streams;
use crate::apps::{AppsHost, AppsOpError};

/// How the Node.js interpreter of a known major got on a module-stream host.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum NodeOrigin {
    /// A module stream of that major is enabled: the panel installed it, or the module system
    /// did, and the stream commands can remove it.
    Stream,
    /// No stream of that major is enabled: the interpreter came from the plain package
    /// (`dnf install nodejs`) or another route the module commands know nothing of.
    Plain,
    /// The module listing could not be read, so the route is not known.
    Unknown,
}

/// Reads the origin of the interpreter whose major is `major`, from the enabled streams of one
/// cache-only module listing.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when the listing outlived its deadline.
pub(crate) fn node_origin<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    major: &str,
) -> Result<NodeOrigin, AppsOpError> {
    Ok(match offered_nodejs_streams(host, distro)? {
        NodejsStreams::Listed(listing) if listing.enabled.iter().any(|stream| stream == major) => {
            NodeOrigin::Stream
        }
        NodejsStreams::Listed(_) => NodeOrigin::Plain,
        NodejsStreams::Unavailable(_) => NodeOrigin::Unknown,
    })
}
