//! Whether an app is on this host and which version, probed and never remembered.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_distro::{AdapterBinary, DistroAdapter, SiteWebServer};

use crate::apps::parse_marker::parse_marker;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::safe_write::SafeWriteError;

/// What the nginx binary's `-v` prints before the version: `nginx version: nginx/1.24.0 (Ubuntu)`.
const NGINX_VERSION_MARKER: &str = "nginx/";

/// What Apache's `-v` prints before the version: `Server version: Apache/2.4.58 (Ubuntu)`.
const APACHE_VERSION_MARKER: &str = "Apache/";

/// Whether `id` is installed on this host, in the sense the panel acts on.
///
/// A web server is installed when the agent's marker (`AgentPaths::SITE_WEB_SERVER_MARKER`) names
/// it: nginx is on every host as the panel's own front, so its package says nothing about whether
/// the host has chosen it for sites, and a fresh host must list no web server so that the first-run
/// setup happens. Apache additionally needs its binary, so a marker left by a hand-removed package
/// is not reported. PHP is installed when its version's pool directory is (the same test the PHP
/// area uses, so the two can never disagree); Node.js when that major's own interpreter runs and
/// reports that major. A `stat` or one `--version` per app, never a package manager query.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when the interpreter outlived its deadline.
pub(crate) fn is_installed<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<bool, AppsOpError> {
    Ok(match id {
        ValidatedAppId::Nginx => marked_web_server(host) == Some(SiteWebServer::Nginx),
        ValidatedAppId::Apache => {
            marked_web_server(host) == Some(SiteWebServer::Apache)
                && package_present(host, distro, id)?
        }
        _ => package_present(host, distro, id)?,
    })
}

/// The site web server the agent's marker names, `None` while the host has chosen none.
pub(crate) fn marked_web_server<H: AppsHost>(host: &H) -> Option<SiteWebServer> {
    parse_marker(
        host.read_config(Path::new(AgentPaths::SITE_WEB_SERVER_MARKER))
            .as_deref(),
    )
}

/// Whether the files of `id`'s package are on this host, whatever the marker says: a web server's
/// binary, a PHP version's pool directory, the Node.js interpreter of that major (that major's own
/// file, not whichever `node` the host resolves). This is what an
/// install checks after its package unit ran; [`is_installed`] is what the panel is told.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when the interpreter outlived its deadline.
pub(crate) fn package_present<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<bool, AppsOpError> {
    Ok(match id {
        ValidatedAppId::Nginx => host.path_exists(distro.nginx_binary().as_path()),
        ValidatedAppId::Apache => host.path_exists(distro.apache_binary().as_path()),
        ValidatedAppId::Php(version) => {
            host.path_exists(Path::new(&distro.php_fpm_pool_directory(version.as_str())))
        }
        ValidatedAppId::Nodejs(major) => node_version(host, distro, major)?
            .is_some_and(|version| version.split('.').next() == Some(major.as_str())),
    })
}

/// Whether the further packages an install of `id` brings (`nodejs_extra_packages`) are on this
/// host, judged by the file a site's unit runs from them: the npm launcher of a Node.js major on
/// a family that ships npm apart from the interpreter (SUSE). `true` for an app with none.
///
/// Apart from [`package_present`] because the two can differ on a host: the interpreter of a
/// major installed before `npm<major>` was part of the install has no launcher, and a site whose
/// unit runs `npm` would not start (issue #530).
pub(crate) fn extras_present<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> bool {
    match id {
        ValidatedAppId::Nodejs(major) => {
            distro.nodejs_extra_packages(major.as_str()).is_empty()
                || host.path_exists(Path::new(&distro.npm_executable(major)))
        }
        _ => true,
    }
}

/// The version of the installed app `id` as the host reports it, empty when it reports none.
///
/// Best effort by design: a version is information for the operator, and an app whose version
/// could not be read is still installed, so a failed read is an empty string and never a failed
/// listing.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when a program outlived its deadline.
pub(crate) fn installed_version<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<String, AppsOpError> {
    Ok(match id {
        ValidatedAppId::Nginx => {
            // nginx prints its version on standard error.
            read_version(
                host,
                &distro.nginx_binary(),
                "-v",
                true,
                NGINX_VERSION_MARKER,
            )?
        }
        ValidatedAppId::Apache => read_version(
            host,
            &distro.apache_binary(),
            "-v",
            false,
            APACHE_VERSION_MARKER,
        )?,
        ValidatedAppId::Php(version) => version.as_str().to_owned(),
        ValidatedAppId::Nodejs(major) => node_version(host, distro, major)?.unwrap_or_default(),
    })
}

/// The version `node --version` reports for the interpreter of `major` itself, without its
/// leading `v`; `None` when that interpreter does not run. On a family with one package per major
/// each major has a file of its own, so a second major beside the first is found and not
/// mistaken for the first's absence (issue #530).
pub(crate) fn node_version<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    major: &NodeMajor,
) -> Result<Option<String>, AppsOpError> {
    match host.run(&distro.node_binary(major), &["--version"], CallClass::Query) {
        Ok(outcome) if outcome.status == 0 => Ok(Some(
            outcome.stdout.trim().trim_start_matches('v').to_owned(),
        )),
        Ok(_) | Err(SafeWriteError::SpawnFailed { .. }) => Ok(None),
        Err(SafeWriteError::Bound(bound)) => Err(AppsOpError::Package(PackageError::Bound(bound))),
        Err(_) => Ok(None),
    }
}

/// Runs `program flag` and returns the token after `marker` in what it printed, or an empty
/// string when it cannot be run or prints no such token.
fn read_version<H: AppsHost>(
    host: &H,
    program: &AdapterBinary,
    flag: &str,
    on_stderr: bool,
    marker: &str,
) -> Result<String, AppsOpError> {
    match host.run(program, &[flag], CallClass::Query) {
        Ok(outcome) => {
            let text = if on_stderr {
                outcome.stderr
            } else {
                outcome.stdout
            };
            Ok(version_after(&text, marker))
        }
        Err(SafeWriteError::Bound(bound)) => Err(AppsOpError::Package(PackageError::Bound(bound))),
        Err(_) => Ok(String::new()),
    }
}

/// The run of version characters (digits and dots) that follows `marker` in `text`.
fn version_after(text: &str, marker: &str) -> String {
    text.split_once(marker)
        .map(|(_, rest)| {
            rest.chars()
                .take_while(|character| character.is_ascii_digit() || *character == '.')
                .collect()
        })
        .unwrap_or_default()
}

#[cfg(test)]
#[path = "../tests/apps/installed_probe_tests.rs"]
mod tests;
