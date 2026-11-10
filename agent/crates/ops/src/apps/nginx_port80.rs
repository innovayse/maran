//! Keeping nginx off port 80 while Apache is the site web server.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::apps::{AppsHost, AppsOpError};
use crate::safe_write::model::{Reload, Validator};

/// nginx's own configuration check.
const CONFIG_TEST: &str = "-t";

/// The service-manager subcommand that makes a running nginx re-read its configuration.
const RELOAD_SUBCOMMAND: &str = "reload";

/// Takes the panel's port-80 ACME block out of nginx's tree and reloads nginx, so that nginx no
/// longer binds port 80.
///
/// The block is the only thing in the panel's nginx configuration that listens on 80
/// (`installer/nginx/maran-port80.conf`). It is parked, not deleted: its content is written to
/// [`AgentPaths::NGINX_PORT80_SUSPENDED_CONFIG`] first (a name nginx never opens), then the live
/// file is removed through the removal protocol, which validates the resulting tree with `nginx -t`
/// and reloads, and puts the file back if either refuses. Both paths are fixed constants of the
/// agent; nothing the panel sent reaches them.
///
/// Called BEFORE Apache is started: until the reload has taken, nginx still holds port 80 and
/// Apache could not bind it. Idempotent: with no live block there is nothing to do, and the parked
/// copy of an earlier run is kept.
///
/// # Errors
///
/// Returns [`AppsOpError::Php`] with the write protocol's refusal; the live block is then still in
/// place and nginx unchanged.
pub(crate) fn suspend_nginx_port80<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<(), AppsOpError> {
    let Some(block) = host.read_config(Path::new(AgentPaths::NGINX_PORT80_CONFIG)) else {
        return Ok(());
    };
    host.write_record(Path::new(AgentPaths::NGINX_PORT80_SUSPENDED_CONFIG), &block)?;

    let nginx = distro.nginx_binary();
    let service_manager = distro.service_manager();
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, distro.nginx_service()];
    let validator = Validator {
        program: &nginx,
        arguments: &[CONFIG_TEST],
        class: CallClass::Validate,
    };
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };
    if let Err(error) = host.remove_config(
        Path::new(AgentPaths::NGINX_PORT80_CONFIG),
        &validator,
        &reload,
    ) {
        // The live block is back, so the parked copy would only be a second truth.
        let _ = host.remove_record(Path::new(AgentPaths::NGINX_PORT80_SUSPENDED_CONFIG));
        return Err(error.into());
    }
    Ok(())
}

/// Puts the parked port-80 block back into nginx's tree and reloads nginx, the undo of
/// [`suspend_nginx_port80`].
///
/// Called AFTER Apache has been stopped and removed, so that port 80 is free for nginx to bind
/// again. Written through the config-write protocol (validated by `nginx -t`, reloaded, undone on
/// a refusal), and the parked copy is deleted only after that succeeded. Idempotent: with nothing
/// parked there is nothing to restore.
///
/// # Errors
///
/// Returns [`AppsOpError::Php`] with the write protocol's refusal; the parked copy is then kept.
pub(crate) fn restore_nginx_port80<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<(), AppsOpError> {
    let Some(block) = host.read_config(Path::new(AgentPaths::NGINX_PORT80_SUSPENDED_CONFIG)) else {
        return Ok(());
    };

    let nginx = distro.nginx_binary();
    let service_manager = distro.service_manager();
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, distro.nginx_service()];
    let validator = Validator {
        program: &nginx,
        arguments: &[CONFIG_TEST],
        class: CallClass::Validate,
    };
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };
    host.write_config(
        Path::new(AgentPaths::NGINX_PORT80_CONFIG),
        &block,
        &validator,
        &reload,
    )?;
    host.remove_record(Path::new(AgentPaths::NGINX_PORT80_SUSPENDED_CONFIG))
}
