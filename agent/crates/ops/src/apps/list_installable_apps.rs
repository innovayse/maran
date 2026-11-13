//! ListInstallableApps: which apps of the catalogue the operator may install here.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroAdapter, NodejsPackaging};

use crate::apps::app_catalogue::{catalogue, kind_of};
use crate::apps::app_packages::{module_stream_of, packages_of};
use crate::apps::installed_probe::is_installed;
use crate::apps::model::installable_app::InstallableApp;
use crate::apps::model::installable_app_list::InstallableAppList;
use crate::apps::model::nodejs_streams::NodejsStreams;
use crate::apps::offered_nodejs_streams::offered_nodejs_streams;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::{OfferedPackages, query_offered_packages};

/// Lists the apps that can be installed on this host, in catalogue order, each saying whether it
/// already is.
///
/// An app is listed when the closed catalogue AND the host's package sources both carry it, the
/// same test the install applies (issue #466): the packages behind every app are asked of the
/// package manager in one read-only, cache-only run (`crate::packages::query_offered_packages`). An
/// installed app stays in the list even if the sources no longer carry it, because it IS on the
/// host. On a family that ships one `nodejs` package, that package offers exactly the major its
/// candidate version names, and no other. On a family that ships Node.js as module streams, a
/// major is offered when the module list carries a stream of it with the `common` profile (one
/// read-only, cache-only module listing), and the package query is not asked about Node.js.
///
/// **A gap in Node.js never hides the other apps** (issue #532). When the module listing gives no
/// answer, the Node.js apps that are not installed are left out and the reason travels beside the
/// list as [`crate::apps::NodejsUnavailable`]: `NoModuleStreams` when the sources carry no modules
/// and `ListingFailed` when the package manager could not be asked or printed what the adapter
/// does not read. nginx, Apache and PHP are listed either way. The RHEL 10 generation has no
/// module streams and packages Node.js per major, so it is never asked the module question and
/// never answers either reason (issue #542).
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] when what the host offers in packages could not be read (never
/// an empty list) or a program outlived its deadline, the module listing included.
pub fn list_installable_apps<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<InstallableAppList, AppsOpError> {
    let ids = catalogue();
    let streams = if distro.nodejs_packaging() == NodejsPackaging::ModuleStreams {
        Some(offered_nodejs_streams(host, distro)?)
    } else {
        None
    };
    let nodejs_unavailable = match &streams {
        Some(NodejsStreams::Unavailable(reason)) => Some(*reason),
        _ => None,
    };
    let mut packages: Vec<String> = Vec::new();
    for id in &ids {
        if module_stream_of(distro, id).is_some() {
            // Offered by the module listing, not by a package of the same name.
            continue;
        }
        for package in packages_of(distro, id) {
            if !packages.contains(&package) {
                packages.push(package);
            }
        }
    }

    let offered = query_offered_packages(host, distro, &packages)?;

    let mut apps = Vec::new();
    for id in ids {
        let installed = is_installed(host, distro, &id)?;
        if installed || is_offered(distro, &id, &offered, streams.as_ref()) {
            apps.push(InstallableApp {
                kind: kind_of(&id),
                installed,
                id,
            });
        }
    }

    if let Some(reason) = nodejs_unavailable {
        // The panel reads the reason from the response; the operator's log tells it too, once
        // per listing.
        tracing::warn!(
            ?reason,
            "Node.js is not offered on this host; the other apps are listed"
        );
    }
    Ok(InstallableAppList {
        apps,
        nodejs_unavailable,
    })
}

/// Whether the host's sources carry every package behind `id`, or, for a Node.js major on a
/// module-stream family, a stream of that major (`streams`). A module-stream family whose listing
/// gave no answer offers no Node.js major.
fn is_offered(
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    offered: &OfferedPackages,
    streams: Option<&NodejsStreams>,
) -> bool {
    if let (ValidatedAppId::Nodejs(major), Some(streams)) = (id, streams) {
        return match streams {
            NodejsStreams::Listed(listing) => listing
                .offered
                .iter()
                .any(|stream| stream.instance() == major.as_str()),
            NodejsStreams::Unavailable(_) => false,
        };
    }
    let carried = packages_of(distro, id)
        .iter()
        .all(|package| offered.names.contains(package));
    match id {
        ValidatedAppId::Nodejs(major)
            if distro.nodejs_packaging() == NodejsPackaging::SingleDistroPackage =>
        {
            carried
                && distro.nodejs_candidate_major(&offered.raw).as_deref() == Some(major.as_str())
        }
        _ => carried,
    }
}

#[cfg(test)]
#[path = "../tests/apps/list_installable_apps_tests.rs"]
mod tests;
