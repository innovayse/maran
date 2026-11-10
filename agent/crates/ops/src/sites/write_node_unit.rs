//! Writing the systemd unit that runs a Node.js site's application.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::node_program::NodeProgram;
use maran_distro::DistroAdapter;
use maran_templates::systemd::node_site_unit::NodeSiteUnit;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::node_unit_path::{node_unit_name, node_unit_path};
use crate::sites::render_vhost::render_failed;
use crate::sites::{SiteHost, SitesOpError};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The subcommand that makes the service manager re-read its unit files.
const DAEMON_RELOAD: &str = "daemon-reload";

/// The subcommand that makes a unit start at boot.
const ENABLE: &str = "enable";

/// The option that starts the unit now as well as at boot.
const START_NOW: &str = "--now";

/// The subcommand that applies a changed unit file to a running application.
const RESTART: &str = "restart";

/// Brings the unit of `input`'s Node.js site to what the site says, and starts it.
///
/// Does nothing for a site that is not a Node.js one. The unit goes through the config-write
/// protocol like every other file this agent writes into `/etc`, with `daemon-reload` as the
/// validator and, as the reload step, `enable --now` for a unit that is new and `restart` for
/// one whose text changed: a unit that cannot be parsed, or an application that refuses to
/// start, puts the previous unit file back and fails, leaving no half-installed service.
///
/// Written BEFORE the vhost that proxies to it, so the web server never reloads a vhost whose
/// upstream is not there yet, and idempotent: a unit that already says what it should is
/// neither rewritten nor restarted, so a retry costs one read.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when the template fails, and what
/// [`SiteHost::read_config`] and [`SiteHost::write_config`] document: a refusal of the
/// validator or of the start with the previous unit restored, or a bound.
pub(crate) fn write_node_unit(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
    paths: &SitePaths,
) -> Result<(), SitesOpError> {
    let SiteKind::Node {
        version,
        port,
        start_command,
    } = &input.kind
    else {
        return Ok(());
    };

    let executable = match start_command.program() {
        NodeProgram::Node => distro.node_executable(),
        NodeProgram::Npm => distro.npm_executable(),
    };
    let document_root = paths.document_root.display().to_string();
    let home_directory = format!(
        "{}/{}",
        AgentPaths::ACCOUNT_HOME_ROOT,
        input.account.as_str()
    );
    let contents = NodeSiteUnit {
        account: input.account.as_str(),
        domain: input.domain.as_str(),
        node_major: version.as_str(),
        document_root: &document_root,
        home_directory: &home_directory,
        port: port.value(),
        executable,
        arguments: start_command.arguments(),
    }
    .render_config()
    .map_err(render_failed)?;

    let target = node_unit_path(distro, &input.domain);
    let current = host.read_config(&target)?;
    if current.as_deref() == Some(contents.as_str()) {
        return Ok(());
    }

    let unit = node_unit_name(&input.domain);
    // The absolute path of the service manager, from the adapter: `ops` names no binary path
    // of its own, and a bare `"systemctl"` would be resolved through `PATH` by a root process.
    let service_manager = distro.service_manager();
    let validator = Validator {
        program: &service_manager,
        arguments: &[DAEMON_RELOAD],
        // `daemon-reload` mutates the service manager's state, so it is a `Reload` and not
        // a pure validation.
        class: CallClass::Reload,
    };
    // `systemctl` honours the end-of-options marker, so the unit name can never be read as an
    // option.
    let first_start = [ENABLE, START_NOW, END_OF_OPTIONS, unit.as_str()];
    let changed = [RESTART, END_OF_OPTIONS, unit.as_str()];
    let reload = Reload {
        program: &service_manager,
        arguments: if current.is_none() {
            &first_start
        } else {
            &changed
        },
    };

    host.write_config(&target, &contents, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/sites/write_node_unit_tests.rs"]
mod tests;
