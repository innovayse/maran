//! Which Node.js module streams the host's package sources offer, and which one is enabled.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroAdapter, PackageQueryFailure};

use crate::apps::model::nodejs_streams::NodejsStreams;
use crate::apps::model::nodejs_unavailable::NodejsUnavailable;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::safe_write::SafeWriteError;

/// Asks the package manager's module listing for the Node.js streams it offers with the profile
/// the panel installs, and for the stream the host has enabled.
///
/// One read-only, cache-only run in the `Query` class, read by the adapter with its exit status
/// and both output streams. A listing the host could not give is [`NodejsStreams::Unavailable`]
/// with a typed reason, never an empty list ("cannot look" is not "nothing offered"), and it is
/// the CALLER's to weigh: a gap in Node.js must not take the other apps down with it (issue #532,
/// a release without modules and a cache never built both end here).
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when the listing outlived its deadline: a
/// wedged package manager is not a reason the Node.js apps alone are missing.
pub(crate) fn offered_nodejs_streams<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<NodejsStreams, AppsOpError> {
    // A host whose packaging has no module streams (the RHEL 10 generation, every other family)
    // has no listing to ask for: it is told so, and the package manager is never run bare.
    let Some(arguments) = distro.nodejs_stream_list_arguments() else {
        return Ok(NodejsStreams::Unavailable(
            NodejsUnavailable::NoModuleStreams,
        ));
    };
    let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
    let outcome = match host.run(&distro.package_query_binary(), &arguments, CallClass::Query) {
        Ok(outcome) => outcome,
        Err(SafeWriteError::Bound(bound)) => {
            return Err(AppsOpError::Package(PackageError::Bound(bound)));
        }
        Err(_) => return Ok(NodejsStreams::Unavailable(NodejsUnavailable::ListingFailed)),
    };
    Ok(
        match distro.read_nodejs_streams(outcome.status, &outcome.stdout, &outcome.stderr) {
            Ok(listing) => NodejsStreams::Listed(listing),
            Err(PackageQueryFailure::NoModules) => {
                NodejsStreams::Unavailable(NodejsUnavailable::NoModuleStreams)
            }
            Err(_) => NodejsStreams::Unavailable(NodejsUnavailable::ListingFailed),
        },
    )
}
