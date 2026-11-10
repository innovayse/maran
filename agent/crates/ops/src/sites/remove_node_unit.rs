//! Taking away the systemd unit of a Node.js site's application.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::node_unit_path::{node_unit_name, node_unit_path};
use crate::sites::{SiteHost, SitesOpError};

/// The subcommand that makes the service manager re-read its unit files.
const DAEMON_RELOAD: &str = "daemon-reload";

/// The subcommand that stops a unit and removes its boot-time link.
const DISABLE: &str = "disable";

/// The option that stops the unit now as well.
const STOP_NOW: &str = "--now";

/// Stops `domain`'s application and removes its unit, when it has one.
///
/// Callable for any site: a site with no unit (every site that is not a Node.js one) is a
/// single read and nothing else, which is what lets `delete_site` and `disable_site` call it
/// without knowing the site's kind. The removal goes through the config-write protocol, with
/// `daemon-reload` as the validator and `disable --now` as the reload step; a refusal puts
/// the unit file back.
///
/// # Errors
///
/// Returns what [`SiteHost::read_config`] and [`SiteHost::remove_config`] document.
pub(crate) fn remove_node_unit(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    domain: &Domain,
) -> Result<(), SitesOpError> {
    let target = node_unit_path(distro, domain);
    if host.read_config(&target)?.is_none() {
        return Ok(());
    }

    let unit = node_unit_name(domain);
    let service_manager = distro.service_manager();
    let validator = Validator {
        program: &service_manager,
        arguments: &[DAEMON_RELOAD],
        class: CallClass::Reload,
    };
    let arguments = [DISABLE, STOP_NOW, END_OF_OPTIONS, unit.as_str()];
    let reload = Reload {
        program: &service_manager,
        arguments: &arguments,
    };

    host.remove_config(&target, &validator, &reload)
}
