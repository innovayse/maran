//! Making a freshly installed Apache serve the panel's vhosts.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroAdapter, SiteWebServer};
use maran_templates::apache::panel_acme_site::PanelAcmeSite;

use crate::apps::marker_text::marker_text;
use crate::apps::nginx_port80::{restore_nginx_port80, suspend_nginx_port80};
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::php::PhpOpError;
use crate::safe_write::SafeWriteError;
use crate::safe_write::model::{Reload, Validator};

/// The mode of the agent's vhost directory: root writes it, the web server reads it.
const INCLUDE_DIRECTORY_MODE: u32 = 0o755;

/// The service-manager subcommand that enables a unit and starts it now.
const ENABLE_SUBCOMMAND: &str = "enable";

/// The flag that also starts the unit now, rather than only at the next boot.
const START_NOW: &str = "--now";

/// The reload that does nothing for a service that is not running yet.
const TRY_RELOAD_SUBCOMMAND: &str = "try-reload-or-restart";

/// Apache's own configuration check.
const CONFIG_TEST: &str = "-t";

/// The log the panel's challenge virtual host writes its access lines to.
const ACME_ACCESS_LOG: &str = "/var/log/maran/apache-acme-access.log";

/// The log the panel's challenge virtual host writes its errors to.
const ACME_ERROR_LOG: &str = "/var/log/maran/apache-acme-error.log";

/// The snippet that makes the packaged configuration include the agent's files.
///
/// The agent's files are never in the distribution's `sites-enabled`, `conf.d` or `vhosts.d`,
/// which belong to its packaging; the packaged configuration gets this snippet, once: the
/// panel's challenge virtual host and one `IncludeOptional` of the directory every site vhost the
/// agent writes later lands in. Both are optional includes, so a missing file is not an error.
fn include_snippet() -> String {
    format!(
        "# Written by the Maran agent; do not edit.\nIncludeOptional {}\nIncludeOptional {}/*.conf\n",
        AgentPaths::APACHE_ACME_CONFIG,
        AgentPaths::APACHE_INCLUDE_DIRECTORY
    )
}

/// Enables the modules the panel's vhost templates use, writes the panel's challenge virtual
/// host (`maran-acme.conf`, when the panel has a host name) and the include of it and of the
/// agent's vhost directory (each validated with Apache's own `-t` and undone if Apache refuses
/// it), then enables and starts the service.
///
/// Apache owns port 80 once it is the site web server, so the answer to the panel's own ACME
/// challenge moves from nginx's port-80 block to this virtual host: the nginx block is taken out of
/// nginx's tree and nginx reloaded BEFORE Apache is started (if Apache then fails to start, the
/// block is put back), and the site web server marker is written last, once Apache runs. A host that records no panel
/// name has no certificate to renew and gets no challenge file.
///
/// Idempotent: each file is written only when it differs, and enabling a module or a service
/// that is already enabled succeeds.
///
/// # Errors
///
/// Returns [`AppsOpError::ServiceManager`] when a module or the service could not be enabled,
/// [`AppsOpError::Php`] with the write protocol's refusal when Apache rejects the include, and
/// [`AppsOpError::Package`] with a bound when a program outlived its deadline.
pub(crate) fn prepare_apache<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    panel_host_name: Option<&str>,
) -> Result<(), AppsOpError> {
    if let Some(enabler) = distro.apache_module_enabler() {
        for module in distro.apache_required_modules() {
            let outcome = host
                .run(&enabler, &[module], CallClass::Mutation)
                .map_err(failed)?;
            if outcome.status != 0 {
                return Err(AppsOpError::ServiceManager {
                    stderr: outcome.stderr,
                });
            }
        }
    }

    host.create_directory(
        Path::new(AgentPaths::APACHE_INCLUDE_DIRECTORY),
        INCLUDE_DIRECTORY_MODE,
    )?;

    if let Some(server_name) = panel_host_name {
        let challenge = PanelAcmeSite {
            server_name,
            challenge_directory: AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY,
            access_log: ACME_ACCESS_LOG,
            error_log: ACME_ERROR_LOG,
        }
        .render_config()
        .map_err(|error| PhpOpError::Render {
            reason: error.to_string(),
        })?;
        write_if_different(
            host,
            distro,
            Path::new(AgentPaths::APACHE_ACME_CONFIG),
            &challenge,
        )?;
    }
    write_if_different(
        host,
        distro,
        Path::new(distro.apache_include_snippet_path()),
        &include_snippet(),
    )?;

    // Apache owns port 80 from here on, and nginx still holds it until its port-80 block is out of
    // its tree and the reload has taken: so this comes BEFORE the service is started, never after.
    suspend_nginx_port80(host, distro)?;

    let service = distro.apache_service();
    let started = host
        .run(
            &distro.service_manager(),
            &[ENABLE_SUBCOMMAND, START_NOW, END_OF_OPTIONS, service],
            CallClass::Reload,
        )
        .map_err(failed)
        .and_then(|outcome| {
            if outcome.status == 0 {
                Ok(())
            } else {
                Err(AppsOpError::ServiceManager {
                    stderr: outcome.stderr,
                })
            }
        });
    if let Err(error) = started {
        // Apache did not take port 80, so nginx gets it back: a failed install must not leave the
        // panel's certificate challenge unanswered. The install's own error stays the one reported.
        let _ = restore_nginx_port80(host, distro);
        return Err(error);
    }

    // Last, after Apache is running: the host has chosen its site web server only once it works.
    host.write_record(
        Path::new(AgentPaths::SITE_WEB_SERVER_MARKER),
        marker_text(SiteWebServer::Apache),
    )?;

    Ok(())
}

/// Writes `contents` to `target` through the config-write protocol, validated by Apache's `-t`
/// and reloaded if it runs, unless the file already holds exactly that.
fn write_if_different<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    target: &Path,
    contents: &str,
) -> Result<(), AppsOpError> {
    if host.read_config(target).as_deref() == Some(contents) {
        return Ok(());
    }

    let validator_program = distro.apache_binary();
    let validator = Validator {
        program: &validator_program,
        arguments: &[CONFIG_TEST],
        class: CallClass::Validate,
    };
    let service_manager = distro.service_manager();
    let reload_arguments = [
        TRY_RELOAD_SUBCOMMAND,
        END_OF_OPTIONS,
        distro.apache_service(),
    ];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };
    host.write_config(target, contents, &validator, &reload)?;
    Ok(())
}

/// Folds a failure to run a program into this area's list, a bound staying a bound.
fn failed(error: SafeWriteError) -> AppsOpError {
    match error {
        SafeWriteError::Bound(bound) => AppsOpError::Package(PackageError::Bound(bound)),
        other => AppsOpError::ServiceManager {
            stderr: other.to_string(),
        },
    }
}

#[cfg(test)]
#[path = "../tests/apps/prepare_apache_tests.rs"]
mod tests;
