//! InstallApp: adding one app of the catalogue to this host.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroAdapter, SiteWebServer};

use crate::apps::app_catalogue::ensure_in_catalogue;
use crate::apps::app_in_use::holds_sites;
use crate::apps::app_packages::packages_of;
use crate::apps::hold_apache_off::{hold_apache_off, unmask_if_masked};
use crate::apps::installed_probe::{
    extras_present, is_installed, marked_web_server, package_present,
};
use crate::apps::marker_text::marker_text;
use crate::apps::prepare_apache::prepare_apache;
use crate::apps::{AppsHost, AppsOpError};
use crate::php::install_php_version;
use crate::php::run_install_unit::run_install_unit;

/// Installs `id` by starting the root install unit for each of its packages, reporting progress
/// as it goes.
///
/// The package manager is NOT run by the agent (its sandbox keeps `/usr` read-only): the agent
/// asks the service manager to start `maran-app-install@<package>` and waits for that run to end.
/// The packages are the adapter's answer for an id that has been through the closed catalogue,
/// so a caller never authors the agent's package list (rules/security.md item 12).
///
/// - **PHP** is the PHP area's own install, whole, so a version installed from the app store and
///   one installed any other way are the same thing (service enabled and started, OPcache
///   drop-in written).
/// - **nginx** is already on every host (it is the panel's front): its install is a check that
///   the other web server is not there, a package install only if the binary is missing, and the
///   write of the site web server marker, which is what makes it "installed" for the panel.
/// - **Apache** is held off the ports before its package is installed where the package starts
///   the service itself (nginx's port-80 block parked, the unit masked; `hold_apache_off`; the
///   hold is undone on every path, and a mask an earlier crashed run left is removed first), and
///   also gets its modules enabled and the include of the agent's vhost directory
///   added, nginx's port-80 block taken out of nginx's tree (nginx reloaded) before its service is
///   enabled and started, and the marker written once it runs (`prepare_apache`).
/// - **Node.js** is its package (and, where the family ships npm apart, that one); a Node.js site
///   runs under a unit of its own.
///
/// A host has at most one site web server, the one the agent's marker names. Installing either is
/// refused while the other is marked (and Apache while nginx holds sites), and the panel's own
/// record refuses the second choice before it asks the agent.
///
/// Idempotent, and visibly so: an app already installed completes at 100% without starting the
/// unit (a package-manager run taken for nothing holds the package database lock for minutes).
/// Success is also checked rather than assumed: after the unit succeeds the app must be there.
///
/// `panel_host_name` is the name the panel itself is served under, which the Apache install
/// needs for the panel's port-80 challenge virtual host; `None` when the host records none.
///
/// `progress` is called with a percentage and a stage name (`"preparing"`, `"download"`,
/// `"install"`, `"enable"`), markers around blocking calls and not measurements.
///
/// # Errors
///
/// Returns [`AppsOpError::UnsupportedApp`] for an id outside the catalogue,
/// [`AppsOpError::WebServerAlreadyInstalled`] for a second site web server,
/// [`AppsOpError::Package`] for a package unit that failed, was refused by a source, could not
/// reach one, or outlived its deadline, [`AppsOpError::StillAbsent`] when the unit succeeded and
/// the app is still not there, and [`AppsOpError::ServiceManager`] or [`AppsOpError::Php`] from
/// the steps after the package.
pub fn install_app<H, P>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    panel_host_name: Option<&str>,
    mut progress: P,
) -> Result<(), AppsOpError>
where
    H: AppsHost,
    P: FnMut(u32, &str),
{
    ensure_in_catalogue(id)?;
    ensure_no_other_web_server(host, id)?;
    if matches!(id, ValidatedAppId::Apache) {
        // A mask left by an install the agent did not survive would fail the enable below on
        // every retry; the hold itself never outlives an install that returns.
        unmask_if_masked(host, distro)?;
    }

    if let ValidatedAppId::Php(version) = id {
        install_php_version(host, distro, version, progress)?;
        return Ok(());
    }

    if is_installed(host, distro, id)? && extras_present(host, distro, id) {
        if matches!(id, ValidatedAppId::Apache) {
            // The retry path converges the include too: an install that stopped between the
            // package and the include gets it here at the cost of one read when it is current.
            prepare_apache(host, distro, panel_host_name)?;
        }
        progress(100, "install");
        return Ok(());
    }

    progress(10, "preparing");
    // Not `is_installed`: for a web server that is the marker, which only this install writes.
    // What is asked here is whether the package is on the host, which for nginx it always is.
    let missing = missing_packages(host, distro, id)?;
    if !missing.is_empty() {
        install_packages(host, distro, id, &missing, &mut progress)?;
    }

    match id {
        ValidatedAppId::Apache => {
            progress(85, "enable");
            prepare_apache(host, distro, panel_host_name)?;
        }
        ValidatedAppId::Nginx => {
            // nginx is already running as the panel's front; installing it for sites is the
            // choice, and the marker is the only record of it.
            progress(85, "enable");
            host.write_record(
                Path::new(AgentPaths::SITE_WEB_SERVER_MARKER),
                marker_text(SiteWebServer::Nginx),
            )?;
        }
        _ => {}
    }

    progress(100, "install");
    Ok(())
}

/// The packages of `id` an install still has to start a unit for: all of them while the app is
/// not on the host, none once it is, and, for a Node.js major whose interpreter is there but whose
/// npm launcher is not (SUSE ships it apart), the packages beyond the interpreter alone.
fn missing_packages<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<Vec<String>, AppsOpError> {
    let packages = packages_of(distro, id);
    if !package_present(host, distro, id)? {
        return Ok(packages);
    }
    if extras_present(host, distro, id) {
        return Ok(Vec::new());
    }
    // `packages_of` lists the main package first; the rest are the extras that are absent.
    Ok(packages.into_iter().skip(1).collect())
}

/// Starts the install unit of each of `packages` (the missing ones of `id`), and checks after
/// each that the app is there, and after the last that everything an install brings is.
///
/// Apache is held off first on a family whose package starts the service in its own maintainer
/// script (`hold_apache_off`): nginx's port-80 block is parked and the unit masked BEFORE the
/// package exists, because that start would otherwise fail on the port nginx holds and leave the
/// package half configured (issue #529). The hold is released on every path out of this function,
/// success, failure and panic alike (it is a guard, `ApacheHold`): the unit is unmasked and
/// nginx gets its port-80 block back at once, and `prepare_apache` parks it again right before
/// it starts Apache, so port 80 is off nginx for the package step and the start only.
fn install_packages<H, P>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    packages: &[String],
    progress: &mut P,
) -> Result<(), AppsOpError>
where
    H: AppsHost,
    P: FnMut(u32, &str),
{
    let hold = if matches!(id, ValidatedAppId::Apache) {
        Some(hold_apache_off(host, distro)?)
    } else {
        None
    };

    let installed: Result<(), AppsOpError> = (|| {
        for package in packages {
            progress(30, "download");
            let unit = run_install_unit(host, distro, package)?;
            progress(70, "install");
            if !package_present(host, distro, id)? {
                return Err(AppsOpError::StillAbsent {
                    app: id.as_id(),
                    detail: format!("its package manager output is in `journalctl -u {unit}`"),
                });
            }
        }
        if !extras_present(host, distro, id) {
            return Err(AppsOpError::StillAbsent {
                app: id.as_id(),
                detail: "the package units succeeded but the app's launcher is still missing"
                    .to_owned(),
            });
        }
        Ok(())
    })();

    let Some(hold) = hold else {
        return installed;
    };
    // The hold is undone on every path, success included; the install's own error stays the
    // reported one when both fail.
    installed.and(hold.release())
}

/// Refuses a second site web server.
///
/// The other server is "there" when the agent's marker names it. A host whose nginx already holds
/// sites but has no marker (installed before the marker existed) also refuses Apache: its sites
/// are served by nginx whatever the record says.
fn ensure_no_other_web_server<H: AppsHost>(
    host: &H,
    id: &ValidatedAppId,
) -> Result<(), AppsOpError> {
    let (other, other_server) = match id {
        ValidatedAppId::Nginx => (ValidatedAppId::Apache, SiteWebServer::Apache),
        ValidatedAppId::Apache => (ValidatedAppId::Nginx, SiteWebServer::Nginx),
        _ => return Ok(()),
    };
    let taken = marked_web_server(host) == Some(other_server)
        || (matches!(other, ValidatedAppId::Nginx) && holds_sites(host, &other)?);
    if taken {
        return Err(AppsOpError::WebServerAlreadyInstalled {
            requested: id.as_id(),
            installed: other.as_id(),
        });
    }
    Ok(())
}

#[cfg(test)]
#[path = "../tests/apps/install_app_tests.rs"]
mod tests;
