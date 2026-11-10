//! UninstallApp: removing one app of the catalogue from this host.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::DistroAdapter;

use crate::apps::app_catalogue::ensure_in_catalogue;
use crate::apps::app_in_use::ensure_not_in_use;
use crate::apps::app_packages::packages_of;
use crate::apps::installed_probe::{is_installed, package_present};
use crate::apps::nginx_port80::restore_nginx_port80;
use crate::apps::{AppsHost, AppsOpError};
use crate::php::run_install_unit::run_remove_unit;

/// Removes `id` by starting the root removal unit for each of its packages, in reverse install
/// order, reporting progress as it goes.
///
/// Refused while the app is in use (`ensure_not_in_use`): a web server while its vhost directory
/// holds a site, a PHP version while an account has a pool on it. The removal unit runs `remove`
/// and not `purge`, so the panel's own configuration, which the package does not own, outlives a
/// reinstall.
///
/// **nginx is never removed**: it is the panel's own front on port 8443, and its package stays.
/// Uninstalling the `nginx` app checks that no site is served by it and then clears the site web
/// server marker, removing nothing else, which is what lets the panel switch its site web server
/// to Apache. Removing **Apache** puts nginx's port-80 block back and reloads nginx once Apache is
/// gone, then clears the marker.
///
/// Not idempotent in the install's sense: removing an app that is not installed is
/// [`AppsOpError::NotInstalled`], because the panel acts on that answer (its ledger and the host
/// disagree) rather than on a silent success.
///
/// `progress` is called with `"preparing"`, `"remove"` and a final `"remove"`.
///
/// # Errors
///
/// Returns [`AppsOpError::UnsupportedApp`] for an id outside the catalogue,
/// [`AppsOpError::NotInstalled`] for an app that is not there, [`AppsOpError::InUse`] for an app
/// something still uses, [`AppsOpError::Package`] for a removal unit that failed or outlived its
/// deadline, and [`AppsOpError::StillAbsent`] when the unit succeeded and the app is still there.
pub fn uninstall_app<H, P>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    mut progress: P,
) -> Result<(), AppsOpError>
where
    H: AppsHost,
    P: FnMut(u32, &str),
{
    ensure_in_catalogue(id)?;
    if !is_installed(host, distro, id)? {
        return Err(AppsOpError::NotInstalled { app: id.as_id() });
    }
    ensure_not_in_use(host, distro, id)?;

    progress(10, "preparing");
    if matches!(id, ValidatedAppId::Nginx) {
        // The package stays: nginx is the panel's front. What goes is the choice itself.
        host.remove_record(Path::new(AgentPaths::SITE_WEB_SERVER_MARKER))?;
        progress(100, "remove");
        return Ok(());
    }

    let mut packages = packages_of(distro, id);
    packages.reverse();
    for package in packages {
        progress(40, "remove");
        run_remove_unit(host, distro, &package)?;
    }

    if package_present(host, distro, id)? {
        return Err(AppsOpError::StillAbsent {
            app: id.as_id(),
            detail: "the removal unit succeeded but the app is still installed".to_owned(),
        });
    }

    if matches!(id, ValidatedAppId::Apache) {
        // Apache is stopped and gone, so port 80 is free: nginx gets its ACME block back and
        // reloads, and only then is the choice cleared. A refusal here keeps the marker, so the
        // state still says what is true (Apache chosen, nginx's block parked) and a retry converges.
        restore_nginx_port80(host, distro)?;
        host.remove_record(Path::new(AgentPaths::SITE_WEB_SERVER_MARKER))?;
    }

    progress(100, "remove");
    Ok(())
}

#[cfg(test)]
#[path = "../tests/apps/uninstall_app_tests.rs"]
mod tests;
