//! ListInstallableApps: which apps of the catalogue the operator may install here.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroAdapter, NodejsPackaging};

use crate::apps::app_catalogue::{catalogue, kind_of};
use crate::apps::app_packages::packages_of;
use crate::apps::installed_probe::is_installed;
use crate::apps::model::installable_app::InstallableApp;
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
/// candidate version names, and no other.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] when what the host offers could not be read (never an empty
/// list) or a program outlived its deadline.
pub fn list_installable_apps<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<Vec<InstallableApp>, AppsOpError> {
    let ids = catalogue();
    let mut packages: Vec<String> = Vec::new();
    for id in &ids {
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
        if installed || is_offered(distro, &id, &offered) {
            apps.push(InstallableApp {
                kind: kind_of(&id),
                installed,
                id,
            });
        }
    }

    Ok(apps)
}

/// Whether the host's sources carry every package behind `id`.
fn is_offered(distro: &dyn DistroAdapter, id: &ValidatedAppId, offered: &OfferedPackages) -> bool {
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
