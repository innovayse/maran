//! ListApps: which apps of the catalogue this host has.

use maran_distro::DistroAdapter;

use crate::apps::app_catalogue::{catalogue, kind_of};
use crate::apps::app_packages::service_of;
use crate::apps::installed_probe::{installed_version, is_installed};
use crate::apps::model::app_service_status::AppServiceStatus;
use crate::apps::model::installed_app::InstalledApp;
use crate::apps::service_status::read_service_status;
use crate::apps::{AppsHost, AppsOpError};

/// Lists the installed apps in catalogue order, each with its version and service status.
///
/// Everything is PROBED at the time of the call: a `stat` per app for installed-ness, one
/// `--version` where the version is not in the id, and one `systemctl is-active` per installed
/// service. Nothing is remembered, so an app installed or removed by hand is reported as it is.
/// A probe that cannot complete fails the whole listing; a shorter list would tell the panel an
/// app is absent that is only unreadable.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when a program outlived its deadline, and
/// [`AppsOpError::ServiceManager`] when the service manager could not be asked.
pub fn list_apps<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<Vec<InstalledApp>, AppsOpError> {
    let mut installed = Vec::new();
    for id in catalogue() {
        if !is_installed(host, distro, &id)? {
            continue;
        }
        let service_status = match service_of(distro, &id) {
            Some(unit) => read_service_status(host, distro, &unit)?,
            None => AppServiceStatus::None,
        };
        installed.push(InstalledApp {
            kind: kind_of(&id),
            version: installed_version(host, distro, &id)?,
            service_status,
            id,
        });
    }

    Ok(installed)
}

#[cfg(test)]
#[path = "../tests/apps/list_apps_tests.rs"]
mod tests;
