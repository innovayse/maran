//! InstallApp: adding one app of the catalogue to this host.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::{DistroAdapter, SiteWebServer};

use crate::apps::app_catalogue::ensure_in_catalogue;
use crate::apps::app_in_use::holds_sites;
use crate::apps::app_packages::{module_stream_of, packages_of};
use crate::apps::hold_apache_off::{hold_apache_off, unmask_if_masked};
use crate::apps::installed_node_major::installed_node_major;
use crate::apps::installed_probe::{
    extras_present, is_installed, marked_web_server, package_present,
};
use crate::apps::marker_text::marker_text;
use crate::apps::model::nodejs_streams::NodejsStreams;
use crate::apps::node_origin::{NodeOrigin, node_origin};
use crate::apps::nodejs_module_operation::take_nodejs_module_operation;
use crate::apps::offered_nodejs_streams::offered_nodejs_streams;
use crate::apps::prepare_apache::prepare_apache;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::run_nodejs_install_unit;
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
///   runs under a unit of its own. Where the family ships Node.js as module streams, the major is
///   one module stream installed by the Node.js module unit, and a host keeps one stream at a
///   time: a different major already on the host (the panel's or an unmanaged one) refuses the
///   install, and the same major is the idempotent no-op.
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
/// [`AppsOpError::NodeMajorAlreadyInstalled`] for a second Node.js major on a module-stream family,
/// [`AppsOpError::UnmanagedNodeInstalled`] when that major was not installed through a module
/// stream, [`AppsOpError::NodeStreamEnabledByOperator`] when another stream is enabled by hand with
/// nothing installed, [`AppsOpError::NodeInterpreterUnreadable`] when the interpreter there reports no
/// version, [`AppsOpError::NodejsOperationInProgress`] while another Node.js module operation runs,
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
    // First, and held to the end: the Node.js check below and the unit it authorises are one
    // critical section, so two installs of different majors cannot both pass it.
    let _module_operation = if module_stream_of(distro, id).is_some() {
        Some(take_nodejs_module_operation(host)?)
    } else {
        None
    };
    ensure_no_other_web_server(host, id)?;
    ensure_no_other_node_major(host, distro, id)?;
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
    if let Some(stream) = module_stream_of(distro, id) {
        // One module stream, enabled and installed by one unit; there is no package list to
        // subtract from, so the question is only whether the interpreter of that major is there.
        if !package_present(host, distro, id)? {
            install_module_stream(host, distro, id, &stream, &mut progress)?;
        }
        progress(100, "install");
        return Ok(());
    }
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

/// Starts the Node.js module unit of `stream` and checks that the interpreter of `id` is there
/// after it.
fn install_module_stream<H, P>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    stream: &NodejsModuleStream,
    progress: &mut P,
) -> Result<(), AppsOpError>
where
    H: AppsHost,
    P: FnMut(u32, &str),
{
    progress(30, "download");
    let unit = run_nodejs_install_unit(host, distro, stream)?;
    progress(70, "install");
    if !package_present(host, distro, id)? || !extras_present(host, distro, id) {
        return Err(AppsOpError::StillAbsent {
            app: id.as_id(),
            detail: format!("its package manager output is in `journalctl -u {unit}`"),
        });
    }
    Ok(())
}

/// Refuses a second Node.js major on a family that keeps one module stream at a time.
///
/// What is asked is the interpreter the host resolves (`node --version`), not the panel's record:
/// a Node.js the operator installed by hand blocks the stream switch just as the panel's own
/// does, and the module system would fail the install halfway through otherwise. The same major
/// is not refused (the install is then a no-op). An interpreter that is there and reports no major
/// is refused too (`installed_node_major`): "cannot tell" is not "nothing installed". Other
/// families keep each major beside the others and are never asked.
///
/// A different major is refused in one of two voices. When a stream of it is enabled the panel's
/// own removal (`UninstallApp`) is the way on, and the refusal says a major is active. When none
/// is (the plain `nodejs` package), the stream commands cannot remove it, so the refusal names the
/// command the operator runs.
fn ensure_no_other_node_major<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<(), AppsOpError> {
    let ValidatedAppId::Nodejs(major) = id else {
        return Ok(());
    };
    if module_stream_of(distro, id).is_none() {
        return Ok(());
    }
    let Some(installed) = installed_node_major(host, distro, major)? else {
        return ensure_no_foreign_enabled_stream(host, distro, id);
    };
    if installed == major.as_str() {
        return Ok(());
    }
    Err(match node_origin(host, distro, &installed)? {
        NodeOrigin::Plain => AppsOpError::UnmanagedNodeInstalled {
            requested: id.as_id(),
            installed,
            command: distro.nodejs_unmanaged_removal_command().to_owned(),
        },
        NodeOrigin::Stream | NodeOrigin::Unknown => AppsOpError::NodeMajorAlreadyInstalled {
            requested: id.as_id(),
            installed,
        },
    })
}

/// Refuses an install that would reset a Node.js module stream an operator enabled.
///
/// The install unit resets the `nodejs` module before it installs the stream asked for. With
/// nothing installed, an enabled stream of ANOTHER major is a choice somebody made by hand
/// (`module enable`), and the reset would silently drop it. The panel installed none, so it
/// leaves the choice alone and names the command. A listing that could not be read does not
/// refuse: nothing says there is a choice to protect, and the unit's own dnf run refuses what it
/// cannot do.
fn ensure_no_foreign_enabled_stream<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<(), AppsOpError> {
    let ValidatedAppId::Nodejs(major) = id else {
        return Ok(());
    };
    let NodejsStreams::Listed(listing) = offered_nodejs_streams(host, distro)? else {
        return Ok(());
    };
    match listing
        .enabled
        .into_iter()
        .find(|enabled| enabled != major.as_str())
    {
        Some(enabled) => Err(AppsOpError::NodeStreamEnabledByOperator {
            requested: id.as_id(),
            enabled,
            command: distro.nodejs_stream_release_command().to_owned(),
        }),
        None => Ok(()),
    }
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
